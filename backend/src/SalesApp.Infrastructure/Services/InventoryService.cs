using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Data.Configurations;

namespace SalesApp.Infrastructure.Services;

public interface IInventoryService
{
    Task<StockSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
    Task<PagedResult<StockLevelDto>> ListLevelsAsync(StockLevelListRequest request, CancellationToken cancellationToken);
    Task<PagedResult<StockMovementDto>> ListMovementsAsync(StockMovementListRequest request, CancellationToken cancellationToken);
    Task<PagedResult<StockReceiptListItemDto>> ListReceiptsAsync(PageRequest request, CancellationToken cancellationToken);
    Task<StockReceiptDto> GetReceiptAsync(int id, CancellationToken cancellationToken);
    Task<StockReceiptDto> CreateReceiptAsync(StockReceiptCreateRequest request, CancellationToken cancellationToken);
    Task<StockMovementDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Stock levels, the movement ledger, goods receipts and adjustments. Every change goes through
/// <see cref="IStockLedger"/>; this service adds the business rules and the read models.
/// </summary>
public sealed class InventoryService(
    AppDbContext db,
    IStockLedger ledger,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IInventoryService
{
    private const string NextReceiptNumberSql =
        "SELECT NEXT VALUE FOR [dbo].[" + StockReceiptConfiguration.NumberSequence + "] AS [Value]";

    private static readonly SortMap<Product> LevelSorts = new SortMap<Product>()
        .Add("name", p => p.Name)
        .Add("sku", p => p.Sku)
        .Add("quantityOnHand", p => p.Stock!.QuantityOnHand)
        .Add("lastMovement", p => p.Stock!.LastMovementAtUtc);

    public async Task<StockSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var active = db.Products.AsNoTracking().Where(p => p.IsActive);
        var summary = await active
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Low = g.Count(p => p.Stock!.QuantityOnHand > 0 && p.Stock.QuantityOnHand <= p.ReorderLevel),
                Out = g.Count(p => p.Stock!.QuantityOnHand <= 0),
                Value = g.Sum(p => p.Stock!.QuantityOnHand > 0 && p.CostPrice != null ? p.Stock.QuantityOnHand * p.CostPrice.Value : 0m),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new StockSummaryDto(
            summary?.Count ?? 0,
            summary?.Low ?? 0,
            summary?.Out ?? 0,
            currentUser.IsManagerOrAdmin ? Math.Round(summary?.Value ?? 0m, 2) : null);
    }

    public async Task<PagedResult<StockLevelDto>> ListLevelsAsync(StockLevelListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();

        if (!request.IncludeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        query = request.Status switch
        {
            StockStatus.OutOfStock => query.Where(p => p.Stock!.QuantityOnHand <= 0),
            StockStatus.Low => query.Where(p => p.Stock!.QuantityOnHand <= p.ReorderLevel),
            StockStatus.InStock => query.Where(p => p.Stock!.QuantityOnHand > p.ReorderLevel && p.Stock.QuantityOnHand > 0),
            _ => query,
        };

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(p => p.Sku.Contains(q) || p.Name.Contains(q));
        }

        var showValue = currentUser.IsManagerOrAdmin;
        var page = await LevelSorts.Apply(query, request.Sort, "name")
            .ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, CategoryName = p.Category!.Name, p.Unit,
                OnHand = p.Stock != null ? p.Stock.QuantityOnHand : 0m,
                p.ReorderLevel, p.ReorderQuantity, p.CostPrice,
                LastMovement = p.Stock != null ? p.Stock.LastMovementAtUtc : null,
            })
            .ToPagedResultAsync(request, cancellationToken);

        var items = page.Items.Select(p => new StockLevelDto(
                p.Id, p.Sku, p.Name, p.CategoryName, p.Unit, p.OnHand, p.ReorderLevel, p.ReorderQuantity,
                StatusOf(p.OnHand, p.ReorderLevel), p.LastMovement,
                showValue && p.CostPrice is { } cost ? Math.Round(Math.Max(p.OnHand, 0) * cost, 2) : null))
            .ToList();
        return new PagedResult<StockLevelDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<PagedResult<StockMovementDto>> ListMovementsAsync(StockMovementListRequest request, CancellationToken cancellationToken)
    {
        var query = db.StockMovements.AsNoTracking();

        if (request.ProductId is { } productId)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        if (request.Type is { } type)
        {
            query = query.Where(m => m.Type == type);
        }

        if (request.From is { } from)
        {
            query = query.Where(m => m.MovementDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(m => m.MovementDate <= to);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(m => m.Product!.Sku.Contains(q) || m.Product.Name.Contains(q) ||
                                     (m.ReferenceNumber != null && m.ReferenceNumber.Contains(q)));
        }

        var showCost = currentUser.IsManagerOrAdmin;

        // The ledger always reads newest first; ties broken by posting order.
        return await query
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.Id)
            .Select(m => new StockMovementDto(
                m.Id, m.ProductId, m.Product!.Sku, m.Product.Name, m.Product.Unit, m.Type, m.MovementDate,
                m.Quantity, m.BalanceAfter,
                showCost ? m.UnitCost : null,
                m.Reason, m.ReferenceType, m.ReferenceId, m.ReferenceNumber, m.Notes,
                m.CreatedBy!.FullName, m.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<PagedResult<StockReceiptListItemDto>> ListReceiptsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var query = db.StockReceipts.AsNoTracking();
        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(r => r.Number.Contains(q) ||
                                     (r.SupplierName != null && r.SupplierName.Contains(q)) ||
                                     (r.SupplierReference != null && r.SupplierReference.Contains(q)));
        }

        var showCost = currentUser.IsManagerOrAdmin;
        return await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.Id)
            .Select(r => new StockReceiptListItemDto(
                r.Id, r.Number, r.ReceiptDate, r.SupplierName, r.SupplierReference,
                db.StockMovements.Count(m => m.ReferenceType == StockReferenceTypes.Receipt && m.ReferenceId == r.Id),
                showCost
                    ? db.StockMovements
                        .Where(m => m.ReferenceType == StockReferenceTypes.Receipt && m.ReferenceId == r.Id && m.UnitCost != null)
                        .Sum(m => (decimal?)(m.Quantity * m.UnitCost!.Value))
                    : null,
                r.CreatedBy!.FullName, r.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<StockReceiptDto> GetReceiptAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await db.StockReceipts
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Id, r.Number, r.ReceiptDate, r.SupplierName, r.SupplierReference, r.Notes, CreatedBy = r.CreatedBy!.FullName, r.CreatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Receipt {id} was not found.");

        var showCost = currentUser.IsManagerOrAdmin;
        var lines = await db.StockMovements
            .AsNoTracking()
            .Where(m => m.ReferenceType == StockReferenceTypes.Receipt && m.ReferenceId == id)
            .OrderBy(m => m.Id)
            .Select(m => new StockReceiptLineDto(m.Id, m.ProductId, m.Product!.Sku, m.Product.Name, m.Product.Unit, m.Quantity, showCost ? m.UnitCost : null))
            .ToListAsync(cancellationToken);

        return new StockReceiptDto(receipt.Id, receipt.Number, receipt.ReceiptDate, receipt.SupplierName, receipt.SupplierReference,
            receipt.Notes, receipt.CreatedBy, receipt.CreatedAtUtc, lines);
    }

    public async Task<StockReceiptDto> CreateReceiptAsync(StockReceiptCreateRequest request, CancellationToken cancellationToken)
    {
        var receiptDate = ValidateDate(request.ReceiptDate, "receiptDate");
        await ValidateReceiptLinesAsync(request.Lines, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var next = (await db.Database.SqlQueryRaw<int>(NextReceiptNumberSql).ToListAsync(cancellationToken)).Single();
        var receipt = new StockReceipt
        {
            Number = $"GRN-{next:D5}",
            ReceiptDate = receiptDate,
            SupplierName = Text.NullIfBlank(request.SupplierName),
            SupplierReference = Text.NullIfBlank(request.SupplierReference),
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.StockReceipts.Add(receipt);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var line in request.Lines)
        {
            await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.Receipt, line.Quantity, receiptDate,
                UnitCost: line.UnitCost,
                ReferenceType: StockReferenceTypes.Receipt,
                ReferenceId: receipt.Id,
                ReferenceNumber: receipt.Number,
                Notes: receipt.SupplierReference is null ? null : $"Supplier ref {receipt.SupplierReference}"), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetReceiptAsync(receipt.Id, cancellationToken);
    }

    public async Task<StockMovementDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var date = ValidateDate(request.MovementDate, "movementDate");
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.Id, p.IsActive })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException("productId", "Choose a valid product.");

        var isOpening = request.Reason == AdjustmentReason.OpeningBalance;
        if (isOpening && await db.StockMovements.AnyAsync(m => m.ProductId == product.Id, cancellationToken))
        {
            throw new ValidationException("reason", "This product already has stock history, so it can't take an opening balance. Use a stock count instead.");
        }

        if (request.Mode == AdjustmentMode.Change && request.Quantity == 0)
        {
            throw new ValidationException("quantity", "Enter how much to add (positive) or remove (negative).");
        }

        var posting = new StockPosting(
            product.Id,
            isOpening ? StockMovementType.Opening : StockMovementType.Adjustment,
            request.Quantity,
            date,
            Reason: request.Reason,
            Notes: request.Notes.Trim());

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var movement = request.Mode == AdjustmentMode.SetCount
            ? await ledger.PostCountAsync(posting, request.Quantity, cancellationToken)
              ?? throw new ValidationException("quantity", "The counted quantity matches stock on hand, so there's nothing to adjust.")
            : await ledger.PostAsync(posting, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await db.StockMovements.AsNoTracking()
            .Where(m => m.Id == movement.Id)
            .Select(m => new StockMovementDto(
                m.Id, m.ProductId, m.Product!.Sku, m.Product.Name, m.Product.Unit, m.Type, m.MovementDate,
                m.Quantity, m.BalanceAfter, null, m.Reason, m.ReferenceType, m.ReferenceId, m.ReferenceNumber, m.Notes,
                m.CreatedBy!.FullName, m.CreatedAtUtc))
            .SingleAsync(cancellationToken);
    }

    internal static StockStatus StatusOf(decimal onHand, decimal reorderLevel) =>
        onHand <= 0 ? StockStatus.OutOfStock : onHand <= reorderLevel ? StockStatus.Low : StockStatus.InStock;

    /// <summary>Movements are dated in India time; nothing can be dated in the future or more than a year back.</summary>
    private DateOnly ValidateDate(DateOnly? requested, string field)
    {
        var today = clock.Today;
        var date = requested ?? today;
        if (date > today)
        {
            throw new ValidationException(field, "The date can't be in the future.");
        }

        if (date < today.AddYears(-1))
        {
            throw new ValidationException(field, "The date can't be more than a year ago.");
        }

        return date;
    }

    private async Task ValidateReceiptLinesAsync(List<StockReceiptLineInput> lines, CancellationToken cancellationToken)
    {
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var active = await db.Products.Where(p => ids.Contains(p.Id) && p.IsActive).Select(p => p.Id).ToListAsync(cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!active.Contains(lines[i].ProductId))
            {
                errors[$"lines[{i}].productId"] = ["Choose an active product."];
            }
            else if (!seen.Add(lines[i].ProductId))
            {
                errors[$"lines[{i}].productId"] = ["This product is already on the receipt. Combine the quantities into one line."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }
    }
}
