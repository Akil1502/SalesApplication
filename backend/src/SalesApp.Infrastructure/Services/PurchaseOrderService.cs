using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Data.Configurations;
using SalesApp.Infrastructure.Notifications;

namespace SalesApp.Infrastructure.Services;

public interface IPurchaseOrderService
{
    Task<PagedResult<PurchaseOrderListItemDto>> ListAsync(PurchaseOrderListRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> CreateAsync(PurchaseOrderSaveRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> UpdateAsync(int id, PurchaseOrderSaveRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> PlaceAsync(int id, PurchaseOrderActionRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> ApproveAsync(int id, PurchaseOrderActionRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> RejectAsync(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> CancelAsync(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderDto> CloseAsync(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken);
    Task<StockReceiptDto> ReceiveAsync(int id, GoodsReceiptCreateRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReorderCandidateDto>> ReorderCandidatesAsync(IReadOnlyCollection<int>? productIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseOrderListItemDto>> CreateReorderDraftsAsync(ReorderDraftRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Purchase orders, and goods received against them. Lines are editable while Draft; placing an order above the
/// company's approval limit waits for someone with the approve right. Receiving posts the goods to stock at the
/// order's price and re-averages each product's cost, all in one transaction.
/// </summary>
public sealed class PurchaseOrderService(
    AppDbContext db,
    IDocumentNumbers numbers,
    IStockLedger ledger,
    IInventoryService inventory,
    ILookupService lookups,
    IBusinessClock clock,
    ICurrentUser currentUser,
    INotificationWriter notifications,
    TimeProvider timeProvider) : IPurchaseOrderService
{
    private const string OrderPrefix = "PO";
    private const string NextReceiptNumberSql = "SELECT NEXT VALUE FOR [dbo].[" + StockReceiptConfiguration.NumberSequence + "] AS [Value]";
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>Orders with goods still to come.</summary>
    internal static readonly PurchaseOrderStatus[] Receivable = [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived];

    /// <summary>Orders that count as "on order" for reordering: anything not finished, including drafts.</summary>
    private static readonly PurchaseOrderStatus[] Unfinished =
        [PurchaseOrderStatus.Draft, PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived];

    public async Task<PagedResult<PurchaseOrderListItemDto>> ListAsync(PurchaseOrderListRequest request, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var query = db.PurchaseOrders.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(o => o.SupplierId == supplierId);
        }

        if (request.From is { } from)
        {
            query = query.Where(o => o.OrderDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(o => o.OrderDate <= to);
        }

        if (request.OpenOnly || request.LateOnly)
        {
            query = query.Where(o => Receivable.Contains(o.Status));
        }

        if (request.LateOnly)
        {
            query = query.Where(o => o.ExpectedDate < today);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(o => o.Number.Contains(q) || o.Supplier!.Name.Contains(q) || o.Supplier.Code.Contains(q) ||
                                     (o.SupplierReference != null && o.SupplierReference.Contains(q)));
        }

        var page = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .Select(ListShape)
            .ToPagedResultAsync(request, cancellationToken);

        return new PagedResult<PurchaseOrderListItemDto>([.. page.Items.Select(r => ToListItem(r, today))], page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<PurchaseOrderDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.CreatedBy)
            .Include(o => o.ApprovedBy)
            .Include(o => o.Lines).ThenInclude(l => l.Product!).ThenInclude(p => p.HsnCode)
            .Include(o => o.Lines).ThenInclude(l => l.Product!).ThenInclude(p => p.Stock)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Purchase order {id} was not found.");

        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
        var states = await lookups.GetStatesAsync(cancellationToken);
        var lines = order.Lines.OrderBy(l => l.LineNumber).ToList();
        var totals = GstCalculator.Calculate(lines.Select(ToGstInput), order.IsInterState);
        var tolerance = settings.OverReceiptTolerancePercent;

        var lineDtos = lines.Select((line, index) =>
        {
            var tax = totals.Lines[index];
            return new PurchaseOrderLineDto(
                line.Id, line.LineNumber, line.ProductId, line.Product!.Sku, line.Product.Name, line.Product.HsnCode!.Code, line.Product.Unit,
                line.Quantity, line.UnitPrice, line.GstRate, tax.TaxableValue, tax.TaxAmount, tax.LineTotal,
                line.QuantityReceived, line.QuantityBilled, Math.Max(0m, line.Quantity - line.QuantityReceived),
                MaxReceivable(line, tolerance), line.Product.Stock?.QuantityOnHand ?? 0m);
        }).ToList();

        var receipts = await db.StockReceipts.AsNoTracking()
            .Where(r => r.PurchaseOrderId == id)
            .OrderBy(r => r.ReceiptDate).ThenBy(r => r.Id)
            .Select(r => new PurchaseReceiptSummaryDto(r.Id, r.Number, r.ReceiptDate, r.SupplierReference, r.Lines.Count, r.CreatedBy!.FullName))
            .ToListAsync(cancellationToken);
        var bills = await db.SupplierBills.AsNoTracking()
            .Where(b => b.PurchaseOrderId == id)
            .OrderBy(b => b.BillDate).ThenBy(b => b.Id)
            .Select(b => new PurchaseBillSummaryDto(b.Id, b.Number, b.SupplierInvoiceNumber, b.BillDate, b.GrandTotal, b.Status, b.MatchStatus))
            .ToListAsync(cancellationToken);

        var supplier = order.Supplier!;
        var address = supplier.Address;
        var limit = settings.PurchaseApprovalLimit;
        return new PurchaseOrderDto(
            order.Id, order.Number, order.Status, order.OrderDate, order.ExpectedDate,
            supplier.Id, supplier.Code, supplier.Name, supplier.Gstin,
            new AddressDto(address.Line1, address.Line2, address.City, address.StateCode, states.GetValueOrDefault(address.StateCode)?.Name ?? address.StateCode, address.Pincode),
            supplier.ContactPerson, order.IsInterState, order.SupplierReference, order.Notes, lineDtos,
            totals.TaxableValue, totals.CgstAmount, totals.SgstAmount, totals.IgstAmount, totals.RoundOff, totals.GrandTotal,
            AmountInWords.Rupees(totals.GrandTotal),
            limit is { } l && totals.GrandTotal > l, limit,
            order.SubmittedAtUtc, order.ApprovedBy?.FullName, order.ApprovedAtUtc, order.RejectionReason, order.OrderedAtUtc,
            order.ClosedAtUtc, order.CloseReason, order.CancelledAtUtc, order.CancellationReason,
            IsLate(order.Status, order.ExpectedDate, clock.Today),
            receipts, bills,
            settings.LegalName, settings.Gstin, CompanyAddress(settings, states), settings.PurchaseOrderTerms,
            order.CreatedBy!.FullName, order.CreatedAtUtc, order.UpdatedAtUtc, RowVersions.ToToken(order.RowVersion));
    }

    public async Task<PurchaseOrderDto> CreateAsync(PurchaseOrderSaveRequest request, CancellationToken cancellationToken)
    {
        var order = await CreateDraftAsync(request, cancellationToken);
        return await GetAsync(order.Id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> UpdateAsync(int id, PurchaseOrderSaveRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            throw new ConflictException($"{order.Number} is {Describe(order.Status)} and can no longer be edited.");
        }

        var supplier = await LoadSupplierAsync(request.SupplierId, cancellationToken);
        var orderDate = ValidateDates(request.OrderDate, request.ExpectedDate);
        var lines = await BuildLinesAsync(supplier.Id, request.Lines, cancellationToken);

        order.SupplierId = supplier.Id;
        order.Lines.Clear();
        await ApplyAsync(order, request, supplier, orderDate, lines, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> PlaceAsync(int id, PurchaseOrderActionRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            throw new ConflictException($"Only draft orders can be placed; {order.Number} is {Describe(order.Status)}.");
        }

        var supplier = await LoadSupplierAsync(order.SupplierId, cancellationToken);
        if (order.Lines.Any(l => l.UnitPrice <= 0))
        {
            throw new ValidationException("lines", "Enter a price for every line before placing the order.");
        }

        // GST rates and the supplier's state as they are today, which is when the order goes out.
        var ids = order.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.IsActive, p.HsnCode!.GstRate }).ToDictionaryAsync(p => p.Id, cancellationToken);
        var inactive = products.Values.Where(p => !p.IsActive).Select(p => p.Sku).ToList();
        if (inactive.Count > 0)
        {
            throw new ValidationException("lines", $"These products are no longer active: {string.Join(", ", inactive)}. Edit the order first.");
        }

        foreach (var line in order.Lines)
        {
            line.GstRate = products[line.ProductId].GstRate;
        }

        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
        order.IsInterState = supplier.Address.StateCode != settings.StateCode;
        SetTotals(order);

        var needsApproval = settings.PurchaseApprovalLimit is { } limit && order.GrandTotal > limit;
        order.SubmittedAtUtc = Now;
        order.RejectionReason = null;
        if (needsApproval && !currentUser.Has(Permissions.PurchaseOrdersApprove))
        {
            order.Status = PurchaseOrderStatus.PendingApproval;
        }
        else
        {
            order.Status = PurchaseOrderStatus.Ordered;
            order.OrderedAtUtc = Now;
            if (needsApproval)
            {
                // Placed by someone who may approve it: that is the approval.
                order.ApprovedByUserId = currentUser.UserId;
                order.ApprovedAtUtc = Now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (order.Status == PurchaseOrderStatus.PendingApproval)
        {
            await notifications.RaiseAsync(new NotificationEvent(
                NotificationKind.PurchaseOrderApproval,
                $"Purchase order {order.Number} needs your approval",
                $"{supplier.Name} · {NotificationText.Money(order.GrandTotal)}, above the {NotificationText.Money(settings.PurchaseApprovalLimit ?? 0)} limit",
                $"/purchase-orders/{order.Id}"), cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> ApproveAsync(int id, PurchaseOrderActionRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status != PurchaseOrderStatus.PendingApproval)
        {
            throw new ConflictException($"Only orders waiting for approval can be approved; {order.Number} is {Describe(order.Status)}.");
        }

        order.Status = PurchaseOrderStatus.Ordered;
        order.ApprovedByUserId = currentUser.UserId;
        order.ApprovedAtUtc = Now;
        order.OrderedAtUtc = Now;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> RejectAsync(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status != PurchaseOrderStatus.PendingApproval)
        {
            throw new ConflictException($"Only orders waiting for approval can be sent back; {order.Number} is {Describe(order.Status)}.");
        }

        order.Status = PurchaseOrderStatus.Draft;
        order.SubmittedAtUtc = null;
        order.RejectionReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> CancelAsync(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Ordered))
        {
            throw new ConflictException(order.Status is PurchaseOrderStatus.Cancelled
                ? $"{order.Number} is already cancelled."
                : $"{order.Number} is {Describe(order.Status)}, so it can't be cancelled. Close it instead to stop further deliveries.");
        }

        if (order.Lines.Any(l => l.QuantityReceived > 0))
        {
            throw new ConflictException($"Goods have arrived against {order.Number}. Close it instead to stop further deliveries.");
        }

        if (await db.SupplierBills.AnyAsync(b => b.PurchaseOrderId == id && b.Status != SupplierBillStatus.Cancelled, cancellationToken))
        {
            throw new ConflictException($"Bills are entered against {order.Number}. Cancel them first.");
        }

        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelledAtUtc = Now;
        order.CancellationReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDto> CloseAsync(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (!Receivable.Contains(order.Status))
        {
            throw new ConflictException($"Only orders with goods still to come can be closed; {order.Number} is {Describe(order.Status)}.");
        }

        if (order.Lines.All(l => l.QuantityReceived == 0))
        {
            throw new ConflictException($"Nothing has arrived against {order.Number} yet. Cancel it instead.");
        }

        order.Status = PurchaseOrderStatus.Closed;
        order.ClosedAtUtc = Now;
        order.CloseReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StockReceiptDto> ReceiveAsync(int id, GoodsReceiptCreateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (!Receivable.Contains(order.Status))
        {
            throw new ConflictException(order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval
                ? $"{order.Number} hasn't been placed yet, so nothing can be received against it."
                : $"{order.Number} is {Describe(order.Status)}, so nothing more can be received against it.");
        }

        var today = clock.Today;
        var receiptDate = request.ReceiptDate ?? today;
        if (receiptDate > today)
        {
            throw new ValidationException("receiptDate", "The receipt date can't be in the future.");
        }

        if (receiptDate < order.OrderDate)
        {
            throw new ValidationException("receiptDate", "Goods can't arrive before the order date.");
        }

        var tolerance = await db.CompanySettings.AsNoTracking().Select(s => s.OverReceiptTolerancePercent).SingleAsync(cancellationToken);
        var productIds = order.Lines.Select(l => l.ProductId).ToList();
        var units = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Unit, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<int>();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var input = request.Lines[i];
            var line = order.Lines.FirstOrDefault(l => l.Id == input.PurchaseOrderLineId);
            if (line is null)
            {
                errors[$"lines[{i}].purchaseOrderLineId"] = [$"That line isn't on {order.Number}."];
            }
            else if (!seen.Add(line.Id))
            {
                errors[$"lines[{i}].purchaseOrderLineId"] = ["This line is listed twice. Combine the quantities."];
            }
            else if (input.Quantity <= 0 && input.QuantityRejected <= 0)
            {
                errors[$"lines[{i}].quantity"] = ["Enter what was accepted or rejected."];
            }
            else if (input.QuantityRejected > 0 && Text.NullIfBlank(input.RejectionReason) is null)
            {
                errors[$"lines[{i}].rejectionReason"] = ["Say why it was rejected."];
            }
            else if (input.Quantity > MaxReceivable(line, tolerance))
            {
                var unit = units[line.ProductId];
                errors[$"lines[{i}].quantity"] =
                [
                    $"Only {Quantity(MaxReceivable(line, tolerance))} {unit} more can be received: {Quantity(line.Quantity)} ordered, " +
                    $"{Quantity(line.QuantityReceived)} already received, and at most {tolerance.ToString("0.##", India)}% extra is accepted.",
                ];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }

        var supplierName = await db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => s.Name).SingleAsync(cancellationToken);
        var next = (await db.Database.SqlQueryRaw<int>(NextReceiptNumberSql).ToListAsync(cancellationToken)).Single();
        var receipt = new StockReceipt
        {
            Number = $"GRN-{next:D5}",
            ReceiptDate = receiptDate,
            PurchaseOrderId = order.Id,
            SupplierId = order.SupplierId,
            SupplierName = supplierName,
            SupplierReference = Text.NullIfBlank(request.SupplierReference),
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        };
        foreach (var input in request.Lines)
        {
            var line = order.Lines.Single(l => l.Id == input.PurchaseOrderLineId);
            receipt.Lines.Add(new StockReceiptLine
            {
                PurchaseOrderLineId = line.Id,
                ProductId = line.ProductId,
                Quantity = input.Quantity,
                UnitCost = line.UnitPrice,
                QuantityRejected = input.QuantityRejected,
                RejectionReason = input.QuantityRejected > 0 ? Text.NullIfBlank(input.RejectionReason) : null,
            });
        }

        db.StockReceipts.Add(receipt);
        await db.SaveChangesAsync(cancellationToken);

        // Rejected goods never enter stock, and the order still expects them.
        foreach (var receiptLine in receipt.Lines.Where(l => l.Quantity > 0))
        {
            var line = order.Lines.Single(l => l.Id == receiptLine.PurchaseOrderLineId);
            var movement = await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.Receipt, receiptLine.Quantity, receiptDate,
                UnitCost: line.UnitPrice,
                ReferenceType: StockReferenceTypes.Receipt,
                ReferenceId: receipt.Id,
                ReferenceNumber: receipt.Number,
                Notes: receipt.SupplierReference is null ? $"Against {order.Number}" : $"Against {order.Number}, supplier ref {receipt.SupplierReference}"), cancellationToken);

            await StockCosting.ApplyReceiptAsync(db, line.ProductId, movement.BalanceAfter - receiptLine.Quantity, receiptLine.Quantity, line.UnitPrice, cancellationToken);
            line.QuantityReceived += receiptLine.Quantity;
        }

        order.Status = order.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Received
            : order.Lines.Any(l => l.QuantityReceived > 0) ? PurchaseOrderStatus.PartlyReceived
            : order.Status;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await inventory.GetReceiptAsync(receipt.Id, cancellationToken);
    }

    /// <summary>
    /// Products at or below their reorder level that unfinished orders don't cover; or, given product ids (from the AI
    /// insights' own suggestions), exactly those products, whatever their level.
    /// </summary>
    public async Task<IReadOnlyList<ReorderCandidateDto>> ReorderCandidatesAsync(IReadOnlyCollection<int>? productIds, CancellationToken cancellationToken)
    {
        var chosen = productIds is { Count: > 0 };
        var products = db.Products.AsNoTracking().Where(p => p.IsActive);
        products = chosen ? products.Where(p => productIds!.Contains(p.Id)) : products.Where(p => p.ReorderLevel > 0);
        var rows = await products
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, p.Unit, p.ReorderLevel, p.ReorderQuantity, p.CostPrice,
                OnHand = p.Stock != null ? p.Stock.QuantityOnHand : 0m,
                OnOrder = db.PurchaseOrderLines
                    .Where(l => l.ProductId == p.Id && l.Quantity > l.QuantityReceived &&
                                db.PurchaseOrders.Any(o => o.Id == l.PurchaseOrderId && Unfinished.Contains(o.Status)))
                    .Sum(l => (decimal?)(l.Quantity - l.QuantityReceived)) ?? 0m,
                SupplierId = p.PreferredSupplier != null && p.PreferredSupplier.IsActive ? p.PreferredSupplierId : null,
                SupplierName = p.PreferredSupplier != null && p.PreferredSupplier.IsActive ? p.PreferredSupplier.Name : null,
            })
            .ToListAsync(cancellationToken);

        var due = chosen ? rows : rows.Where(r => r.OnHand <= r.ReorderLevel && r.OnHand + r.OnOrder <= r.ReorderLevel).ToList();
        var prices = new Dictionary<int, decimal?>();
        foreach (var group in due.Where(r => r.SupplierId is not null).GroupBy(r => r.SupplierId!.Value))
        {
            foreach (var (productId, price) in await LastPricesAsync(group.Key, [.. group.Select(r => r.Id)], cancellationToken))
            {
                prices[productId] = price;
            }
        }

        return [.. due
            .OrderBy(r => r.SupplierName ?? "￿").ThenBy(r => r.Sku, StringComparer.Ordinal)
            .Select(r => new ReorderCandidateDto(r.Id, r.Sku, r.Name, r.Unit, r.OnHand, r.ReorderLevel, r.OnOrder,
                SuggestedQuantity(r.ReorderQuantity, r.ReorderLevel, r.OnHand, r.OnOrder),
                r.SupplierId, r.SupplierName, prices.GetValueOrDefault(r.Id)))];
    }

    public async Task<IReadOnlyList<PurchaseOrderListItemDto>> CreateReorderDraftsAsync(ReorderDraftRequest request, CancellationToken cancellationToken)
    {
        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.IsActive, p.LeadTimeDays, p.PreferredSupplierId, SupplierActive = p.PreferredSupplier != null && p.PreferredSupplier.IsActive })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<int>();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var productId = request.Lines[i].ProductId;
            if (!products.TryGetValue(productId, out var product) || !product.IsActive)
            {
                errors[$"lines[{i}].productId"] = ["Choose an active product."];
            }
            else if (!seen.Add(productId))
            {
                errors[$"lines[{i}].productId"] = ["This product is listed twice."];
            }
            else if (product.PreferredSupplierId is null || !product.SupplierActive)
            {
                errors[$"lines[{i}].productId"] = [$"{product.Sku} has no active preferred supplier. Set one on the product first."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some products can't be ordered yet.", errors);
        }

        var today = clock.Today;
        var created = new List<int>();
        foreach (var group in request.Lines.GroupBy(l => products[l.ProductId].PreferredSupplierId!.Value))
        {
            var leadTime = group.Max(l => products[l.ProductId].LeadTimeDays);
            var order = await CreateDraftAsync(new PurchaseOrderSaveRequest
            {
                SupplierId = group.Key,
                OrderDate = today,
                ExpectedDate = leadTime > 0 ? today.AddDays(leadTime) : null,
                Notes = "Drafted from reorder suggestions.",
                Lines = [.. group.Select(l => new PurchaseOrderLineInput { ProductId = l.ProductId, Quantity = l.Quantity })],
            }, cancellationToken);
            created.Add(order.Id);
        }

        var rows = await db.PurchaseOrders.AsNoTracking().Where(o => created.Contains(o.Id)).OrderBy(o => o.Id).Select(ListShape).ToListAsync(cancellationToken);
        return [.. rows.Select(r => ToListItem(r, today))];
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    internal static GstLineInput ToGstInput(PurchaseOrderLine line) => new(line.Quantity, line.UnitPrice, 0m, line.GstRate);

    /// <summary>How much more can arrive against a line: what was ordered plus the tolerance, less what has come.</summary>
    internal static decimal MaxReceivable(PurchaseOrderLine line, decimal tolerancePercent) =>
        Math.Max(0m, Math.Round(line.Quantity * (1 + tolerancePercent / 100m), 3, MidpointRounding.ToZero) - line.QuantityReceived);

    internal static bool IsLate(PurchaseOrderStatus status, DateOnly? expected, DateOnly today) =>
        Receivable.Contains(status) && expected is { } date && date < today;

    /// <summary>Order the reorder quantity, or enough to get back above the reorder level if that's more.</summary>
    internal static decimal SuggestedQuantity(decimal reorderQuantity, decimal reorderLevel, decimal onHand, decimal onOrder)
    {
        var shortfall = reorderLevel - onHand - onOrder;
        var quantity = Math.Max(reorderQuantity, shortfall);
        return quantity > 0 ? quantity : reorderLevel;
    }

    private static string Describe(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.PendingApproval => "waiting for approval",
        PurchaseOrderStatus.PartlyReceived => "partly received",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string Quantity(decimal value) => value.ToString("#,##0.###", India);

    /// <summary>The list row, computed in SQL. Received value is capped at what was ordered, so extra doesn't overstate it.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<PurchaseOrder, ListRow>> ListShape = o => new ListRow(
        o.Id, o.Number, o.Status, o.OrderDate, o.ExpectedDate, o.SupplierId, o.Supplier!.Name, o.Lines.Count, o.GrandTotal,
        o.Lines.Sum(l => (decimal?)(l.Quantity * l.UnitPrice)) ?? 0m,
        o.Lines.Sum(l => (decimal?)((l.QuantityReceived < l.Quantity ? l.QuantityReceived : l.Quantity) * l.UnitPrice)) ?? 0m,
        o.CreatedBy!.FullName);

    private sealed record ListRow(int Id, string Number, PurchaseOrderStatus Status, DateOnly OrderDate, DateOnly? ExpectedDate, int SupplierId,
        string SupplierName, int LineCount, decimal GrandTotal, decimal OrderedValue, decimal ReceivedValue, string CreatedByName);

    private static PurchaseOrderListItemDto ToListItem(ListRow r, DateOnly today) => new(
        r.Id, r.Number, r.Status, r.OrderDate, r.ExpectedDate, r.SupplierId, r.SupplierName, r.LineCount, r.GrandTotal,
        r.OrderedValue == 0 ? 0m : Math.Round(r.ReceivedValue / r.OrderedValue, 4),
        IsLate(r.Status, r.ExpectedDate, today), r.CreatedByName);

    private async Task<PurchaseOrder> CreateDraftAsync(PurchaseOrderSaveRequest request, CancellationToken cancellationToken)
    {
        var supplier = await LoadSupplierAsync(request.SupplierId, cancellationToken);
        var orderDate = ValidateDates(request.OrderDate, request.ExpectedDate);
        var lines = await BuildLinesAsync(supplier.Id, request.Lines, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = new PurchaseOrder
        {
            Number = await numbers.NextAsync(DocumentTypes.PurchaseOrder, OrderPrefix, orderDate, cancellationToken),
            SupplierId = supplier.Id,
            CreatedByUserId = currentUser.UserId,
        };
        await ApplyAsync(order, request, supplier, orderDate, lines, cancellationToken);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return order;
    }

    private async Task ApplyAsync(PurchaseOrder order, PurchaseOrderSaveRequest request, Supplier supplier, DateOnly orderDate,
        List<PurchaseOrderLine> lines, CancellationToken cancellationToken)
    {
        var companyState = await db.CompanySettings.AsNoTracking().Select(s => s.StateCode).SingleAsync(cancellationToken);
        order.OrderDate = orderDate;
        order.ExpectedDate = request.ExpectedDate;
        order.SupplierReference = Text.NullIfBlank(request.SupplierReference);
        order.Notes = Text.NullIfBlank(request.Notes);
        order.IsInterState = supplier.Address.StateCode != companyState;
        order.Lines.AddRange(lines);
        SetTotals(order);
    }

    private static void SetTotals(PurchaseOrder order)
    {
        var totals = GstCalculator.Calculate(order.Lines.Select(ToGstInput), order.IsInterState);
        order.TaxableValue = totals.TaxableValue;
        order.TaxAmount = totals.TaxAmount;
        order.GrandTotal = totals.GrandTotal;
    }

    private async Task<PurchaseOrder> LoadForChangeAsync(int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Purchase order {id} was not found.");
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = RowVersions.Parse(rowVersion);
        return order;
    }

    private async Task<Supplier> LoadSupplierAsync(int supplierId, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
        return supplier is { IsActive: true }
            ? supplier
            : throw new ValidationException("supplierId", "Choose an active supplier.");
    }

    private DateOnly ValidateDates(DateOnly? requestedOrderDate, DateOnly? expectedDate)
    {
        var today = clock.Today;
        var orderDate = requestedOrderDate ?? today;
        if (orderDate > today)
        {
            throw new ValidationException("orderDate", "The order date can't be in the future.");
        }

        if (orderDate < today.AddYears(-1))
        {
            throw new ValidationException("orderDate", "The order date can't be more than a year ago.");
        }

        if (expectedDate is { } expected && expected < orderDate)
        {
            throw new ValidationException("expectedDate", "The expected delivery date can't be before the order date.");
        }

        return orderDate;
    }

    /// <summary>
    /// Products must exist, be active and appear once. A line without a price gets the last price paid to this supplier
    /// for the product, else its cost price; GST comes from the HSN code.
    /// </summary>
    private async Task<List<PurchaseOrderLine>> BuildLinesAsync(int supplierId, List<PurchaseOrderLineInput> inputs, CancellationToken cancellationToken)
    {
        var ids = inputs.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.IsActive, p.CostPrice, GstRate = p.HsnCode!.GstRate })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var lastPrices = await LastPricesAsync(supplierId, ids, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<int>();
        var lines = new List<PurchaseOrderLine>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (!products.TryGetValue(input.ProductId, out var product) || !product.IsActive)
            {
                errors[$"lines[{i}].productId"] = ["Choose an active product."];
                continue;
            }

            if (!seen.Add(input.ProductId))
            {
                errors[$"lines[{i}].productId"] = ["This product is already on the order. Change the quantity on the existing line instead."];
                continue;
            }

            lines.Add(new PurchaseOrderLine
            {
                LineNumber = lines.Count + 1,
                ProductId = input.ProductId,
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice ?? lastPrices.GetValueOrDefault(input.ProductId) ?? product.CostPrice ?? 0m,
                GstRate = product.GstRate,
            });
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }

        return lines;
    }

    /// <summary>
    /// The last price paid to a supplier for each product: from their latest bill, else their latest placed order.
    /// Products never bought from them are left out.
    /// </summary>
    internal async Task<Dictionary<int, decimal?>> LastPricesAsync(int supplierId, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        var billed = await db.SupplierBillLines.AsNoTracking()
            .Where(l => productIds.Contains(l.ProductId) &&
                        db.SupplierBills.Any(b => b.Id == l.SupplierBillId && b.SupplierId == supplierId && b.Status != SupplierBillStatus.Cancelled))
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Price = g.OrderByDescending(l => l.SupplierBillId).Select(l => l.UnitPrice).First() })
            .ToListAsync(cancellationToken);
        var ordered = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => productIds.Contains(l.ProductId) &&
                        db.PurchaseOrders.Any(o => o.Id == l.PurchaseOrderId && o.SupplierId == supplierId &&
                                                   o.Status != PurchaseOrderStatus.Draft && o.Status != PurchaseOrderStatus.Cancelled))
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Price = g.OrderByDescending(l => l.PurchaseOrderId).Select(l => l.UnitPrice).First() })
            .ToListAsync(cancellationToken);

        var prices = ordered.ToDictionary(o => o.ProductId, o => (decimal?)o.Price);
        foreach (var bill in billed)
        {
            prices[bill.ProductId] = bill.Price;
        }

        return prices;
    }

    private static string CompanyAddress(CompanySettings s, IReadOnlyDictionary<string, StateDto> states)
    {
        var state = states.GetValueOrDefault(s.StateCode)?.Name ?? s.StateCode;
        return string.Join(", ", new[] { s.AddressLine1, s.AddressLine2, $"{s.City} - {s.Pincode}", state }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
