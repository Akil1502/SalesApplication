using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Notifications;

namespace SalesApp.Infrastructure.Services;

/// <summary>Inventory settings, bound from the "Inventory" configuration section.</summary>
public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>
    /// Whether sales and adjustments may take stock below zero. Off by default: invoicing goods you
    /// don't have usually means a receipt wasn't entered, and it's better to catch that at the time.
    /// </summary>
    public bool AllowNegativeStock { get; set; }
}

/// <summary>One change to a product's stock, as requested by receipts, adjustments or invoices.</summary>
public sealed record StockPosting(
    int ProductId,
    StockMovementType Type,
    decimal Quantity,
    DateOnly MovementDate,
    decimal? UnitCost = null,
    AdjustmentReason? Reason = null,
    string? ReferenceType = null,
    int? ReferenceId = null,
    string? ReferenceNumber = null,
    string? Notes = null);

public interface IStockLedger
{
    /// <summary>
    /// Applies a signed quantity change and records it in the ledger. Must run inside the caller's
    /// transaction, so the stock level and its ledger line commit (or roll back) together.
    /// </summary>
    Task<StockMovement> PostAsync(StockPosting posting, CancellationToken cancellationToken);

    /// <summary>
    /// Sets stock to a counted quantity and records the difference. Returns null when the count already
    /// matches. The difference is computed inside the UPDATE, so a sale landing mid-count isn't lost.
    /// </summary>
    Task<StockMovement?> PostCountAsync(StockPosting posting, decimal countedQuantity, CancellationToken cancellationToken);
}

/// <summary>
/// The only code that changes stock. Each posting is one atomic UPDATE ... OUTPUT on the product's
/// StockLevels row: the database applies the change, enforces the no-negative rule and returns the new
/// balance in a single statement. Concurrent postings therefore serialise on that row instead of
/// overwriting each other, and BalanceAfter on every ledger line is exact.
/// </summary>
public sealed class StockLedger(
    AppDbContext db,
    ICurrentUser currentUser,
    INotificationWriter notifications,
    TimeProvider timeProvider,
    IOptions<InventoryOptions> options) : IStockLedger
{
    public async Task<StockMovement> PostAsync(StockPosting posting, CancellationToken cancellationToken)
    {
        EnsureInTransaction();
        if (posting.Quantity == 0)
        {
            throw new ValidationException("quantity", "Quantity can't be zero.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Additions are always allowed; only removals are checked against the stock on hand.
        var allowNegative = options.Value.AllowNegativeStock || posting.Quantity > 0;

        // ToListAsync rather than SingleAsync: composing would wrap UPDATE ... OUTPUT in a subquery,
        // which SQL Server rejects. Interpolated values become SQL parameters.
        var quantity = QuantityParameter(posting.Quantity);
        var balances = await db.Database.SqlQuery<decimal>($"""
            UPDATE [dbo].[StockLevels]
            SET [QuantityOnHand] = [QuantityOnHand] + {quantity}, [LastMovementAtUtc] = {now}
            OUTPUT inserted.[QuantityOnHand] AS [Value]
            WHERE [ProductId] = {posting.ProductId}
              AND ({allowNegative} = 1 OR [QuantityOnHand] + {quantity} >= 0)
            """).ToListAsync(cancellationToken);

        if (balances.Count == 0)
        {
            throw await InsufficientStockOrMissingAsync(posting.ProductId, -posting.Quantity, cancellationToken);
        }

        return await RecordAsync(posting, posting.Quantity, balances[0], now, cancellationToken);
    }

    public async Task<StockMovement?> PostCountAsync(StockPosting posting, decimal countedQuantity, CancellationToken cancellationToken)
    {
        EnsureInTransaction();
        if (countedQuantity < 0)
        {
            throw new ValidationException("quantity", "A counted quantity can't be negative.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await db.Database.SqlQuery<CountResult>($"""
            UPDATE [dbo].[StockLevels]
            SET [QuantityOnHand] = {QuantityParameter(countedQuantity)}, [LastMovementAtUtc] = {now}
            OUTPUT deleted.[QuantityOnHand] AS [Previous], inserted.[QuantityOnHand] AS [Current]
            WHERE [ProductId] = {posting.ProductId}
            """).ToListAsync(cancellationToken);

        var row = rows.SingleOrDefault() ?? throw new NotFoundException($"Product {posting.ProductId} was not found.");
        var difference = row.Current - row.Previous;
        return difference == 0 ? null : await RecordAsync(posting, difference, row.Current, now, cancellationToken);
    }

    private async Task<StockMovement> RecordAsync(StockPosting posting, decimal quantity, decimal balanceAfter, DateTime now, CancellationToken cancellationToken)
    {
        var movement = new StockMovement
        {
            ProductId = posting.ProductId,
            Type = posting.Type,
            MovementDate = posting.MovementDate,
            Quantity = quantity,
            BalanceAfter = balanceAfter,
            UnitCost = posting.UnitCost,
            Reason = posting.Reason,
            ReferenceType = posting.ReferenceType,
            ReferenceId = posting.ReferenceId,
            ReferenceNumber = posting.ReferenceNumber,
            Notes = posting.Notes,
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = now,
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        if (quantity < 0)
        {
            await NotifyIfNowLowAsync(posting.ProductId, balanceAfter - quantity, balanceAfter, cancellationToken);
        }

        return movement;
    }

    /// <summary>
    /// Tells people when a product falls to its reorder level: only on the way down past it, so a product that stays
    /// low isn't reported again until it has been restocked above the level.
    /// </summary>
    private async Task NotifyIfNowLowAsync(int productId, decimal before, decimal after, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId && p.IsActive)
            .Select(p => new { p.Name, p.Unit, p.ReorderLevel })
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null || before <= product.ReorderLevel || after > product.ReorderLevel)
        {
            return;
        }

        var (title, body) = NotificationText.LowStock(product.Name, after, product.ReorderLevel, product.Unit);
        await notifications.RaiseAsync(new NotificationEvent(
            NotificationKind.LowStock,
            title,
            body,
            $"/products/{productId}",
            ByCurrentUser: false), cancellationToken);
    }

    private async Task<Exception> InsufficientStockOrMissingAsync(int productId, decimal requested, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.Unit, OnHand = p.Stock != null ? p.Stock.QuantityOnHand : (decimal?)null })
            .SingleOrDefaultAsync(cancellationToken);

        if (product?.OnHand is not { } onHand)
        {
            return new NotFoundException($"Product {productId} was not found.");
        }

        return new ValidationException("quantity",
            $"Only {onHand:0.###} {product.Unit} of {product.Name} in stock; can't remove {requested:0.###}.");
    }

    /// <summary>
    /// Quantities go to SQL as decimal(18,3). EF's default for a decimal parameter in raw SQL is
    /// decimal(18,2), which would round 75.125 kg to 75.13 and let stock drift from the ledger.
    /// </summary>
    private static SqlParameter QuantityParameter(decimal value)
    {
        if (decimal.Round(value, 3) != value)
        {
            throw new ValidationException("quantity", "Use at most 3 decimal places.");
        }

        return new SqlParameter { SqlDbType = SqlDbType.Decimal, Precision = 18, Scale = 3, Value = value };
    }

    private void EnsureInTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Stock postings must run inside a database transaction.");
        }
    }

    /// <summary>Shape of the OUTPUT clause for a stock count.</summary>
    private sealed class CountResult
    {
        public decimal Previous { get; set; }
        public decimal Current { get; set; }
    }
}
