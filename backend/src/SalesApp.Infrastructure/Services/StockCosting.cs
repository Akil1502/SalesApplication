using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Purchasing;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

/// <summary>
/// Keeps each product's cost price at the weighted average of what its stock cost (see <see cref="PurchaseCosting"/>).
/// Runs inside the receipt's or bill's transaction, after the stock posting: that posting holds the product's stock
/// row until commit, so two deliveries of the same product can't average against each other's half-finished state.
/// The caller saves; the change then shows in the product's history like any other.
/// </summary>
internal static class StockCosting
{
    /// <summary>Blends a receipt into the cost. <paramref name="onHandBefore"/> is the stock just before it was posted.</summary>
    public static async Task ApplyReceiptAsync(AppDbContext db, int productId, decimal onHandBefore, decimal quantity, decimal unitCost, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleAsync(p => p.Id == productId, cancellationToken);
        product.CostPrice = PurchaseCosting.AfterReceipt(product.CostPrice, onHandBefore, quantity, unitCost);
    }

    /// <summary>
    /// Adds what a bill changes about a delivery's cost (a different price, a share of freight) to the stock still on
    /// hand. A negative amount takes it back off, when the bill is cancelled.
    /// </summary>
    public static async Task ApplyBillAdjustmentAsync(AppDbContext db, int productId, decimal billedQuantity, decimal extra, CancellationToken cancellationToken)
    {
        if (extra == 0)
        {
            return;
        }

        // Read straight from the database: stock is changed by the ledger's own UPDATEs, never through tracked entities.
        var onHand = await db.StockLevels.AsNoTracking().Where(s => s.ProductId == productId).Select(s => s.QuantityOnHand).SingleOrDefaultAsync(cancellationToken);
        var product = await db.Products.SingleAsync(p => p.Id == productId, cancellationToken);
        product.CostPrice = PurchaseCosting.AfterBillAdjustment(product.CostPrice, onHand, billedQuantity, extra);
    }
}
