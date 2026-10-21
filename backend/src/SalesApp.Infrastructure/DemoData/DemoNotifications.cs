using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Notifications;

namespace SalesApp.Infrastructure.DemoData;

/// <summary>
/// The notifications the last few days of made-up activity would have produced, so the bell in a fresh demo has
/// something in it: payments received, new and confirmed orders, things waiting for approval and low stock.
/// </summary>
internal static class DemoNotifications
{
    private const int Days = 4;

    public static async Task CreateAsync(AppDbContext db, IBusinessClock clock, CancellationToken cancellationToken)
    {
        // From the start of the day, four days ago (India time).
        var since = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(clock.Today.AddDays(-(Days - 1)).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), clock.TimeZone);
        var notifications = new List<Notification>();

        Notification Make(NotificationKind kind, (string Title, string Body) text, string link, DateTime at, int? customerId = null, Guid? actor = null) =>
            new() { Kind = kind, Title = text.Title, Body = text.Body, Link = link, CustomerId = customerId, ActorUserId = actor, CreatedAtUtc = at };

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.CreatedAtUtc >= since)
            .Select(p => new { p.InvoiceId, p.Invoice!.Number, p.Invoice.CustomerId, p.Invoice.CustomerName, p.Amount, p.Mode, p.CreatedByUserId, p.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        notifications.AddRange(payments.Select(p => Make(NotificationKind.PaymentReceived, NotificationText.PaymentReceived(p.Amount, p.CustomerName, p.Mode, p.Number),
            $"/invoices/{p.InvoiceId}", p.CreatedAtUtc, p.CustomerId, p.CreatedByUserId)));

        var orders = await db.SalesOrders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= since || o.ConfirmedAtUtc >= since)
            .Select(o => new { o.Id, o.Number, o.CustomerId, Customer = o.Customer!.Name, o.GrandTotal, Lines = o.Lines.Count, o.CreatedAtUtc, o.ConfirmedAtUtc, o.CreatedByUserId })
            .ToListAsync(cancellationToken);
        foreach (var o in orders)
        {
            if (o.CreatedAtUtc >= since)
            {
                notifications.Add(Make(NotificationKind.OrderCreated, NotificationText.OrderCreated(o.Number, o.Customer, o.Lines, o.GrandTotal),
                    $"/orders/{o.Id}", o.CreatedAtUtc, o.CustomerId, o.CreatedByUserId));
            }

            if (o.ConfirmedAtUtc is { } confirmed && confirmed >= since)
            {
                notifications.Add(Make(NotificationKind.OrderConfirmed, NotificationText.OrderConfirmed(o.Number, o.Customer, o.GrandTotal),
                    $"/orders/{o.Id}", confirmed, o.CustomerId, o.CreatedByUserId));
            }
        }

        var awaiting = await db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Status == PurchaseOrderStatus.PendingApproval)
            .Select(o => new { o.Id, o.Number, Supplier = o.Supplier!.Name, o.GrandTotal, At = o.SubmittedAtUtc ?? o.CreatedAtUtc, o.CreatedByUserId })
            .ToListAsync(cancellationToken);
        notifications.AddRange(awaiting.Select(o => Make(NotificationKind.PurchaseOrderApproval, NotificationText.PurchaseOrderApproval(o.Number, o.Supplier, o.GrandTotal, null),
            $"/purchase-orders/{o.Id}", o.At < since ? since : o.At, actor: o.CreatedByUserId)));

        var onHold = await db.SupplierBills.AsNoTracking()
            .Where(b => b.MatchStatus == BillMatchStatus.NeedsApproval && b.Status == SupplierBillStatus.Unpaid)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(3)
            .Select(b => new { b.Id, b.Number, b.SupplierName, b.GrandTotal, b.CreatedAtUtc, b.CreatedByUserId })
            .ToListAsync(cancellationToken);
        notifications.AddRange(onHold.Select(b => Make(NotificationKind.BillApproval, NotificationText.BillApproval(b.Number, b.SupplierName, null, b.GrandTotal),
            $"/supplier-bills/{b.Id}", b.CreatedAtUtc < since ? since : b.CreatedAtUtc, actor: b.CreatedByUserId)));

        var low = await db.StockLevels.AsNoTracking()
            .Where(s => s.Product!.IsActive && s.QuantityOnHand <= s.Product.ReorderLevel && s.LastMovementAtUtc >= since)
            .Select(s => new { s.ProductId, s.Product!.Name, s.Product.Unit, s.Product.ReorderLevel, s.QuantityOnHand, At = s.LastMovementAtUtc!.Value })
            .ToListAsync(cancellationToken);
        notifications.AddRange(low.Select(s => Make(NotificationKind.LowStock, NotificationText.LowStock(s.Name, s.QuantityOnHand, s.ReorderLevel, s.Unit),
            $"/products/{s.ProductId}", s.At)));

        // Oldest first, so ids grow with time as they would have.
        db.Notifications.AddRange(notifications.OrderBy(n => n.CreatedAtUtc));
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken);
    }
}
