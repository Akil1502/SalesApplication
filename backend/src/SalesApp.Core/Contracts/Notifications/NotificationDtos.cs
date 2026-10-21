using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Localization;

namespace SalesApp.Core.Contracts.Notifications;

/// <summary>One notification for the signed-in person. Important ones (payments received, things waiting for approval) pop up when they arrive.</summary>
public sealed record NotificationDto(long Id, NotificationKind Kind, string Title, string? Body, string Link, DateTime CreatedAtUtc, bool Read, bool Important);

/// <param name="Items">The newest first.</param>
/// <param name="UnreadCount">All unread ones this person may see, not only those listed.</param>
/// <param name="LastOpenedUpToId">Items newer than this arrived since the bell was last opened.</param>
public sealed record NotificationListDto(IReadOnlyList<NotificationDto> Items, int UnreadCount, long LastOpenedUpToId);

/// <summary>A kind this person may receive, and whether they've switched it off.</summary>
public sealed record NotificationKindDto(NotificationKind Kind, string Label, string Description, bool Muted);

public sealed record NotificationPreferencesRequest
{
    [Required]
    public IReadOnlyList<NotificationKind> Muted { get; init; } = [];
}

/// <summary>Up to which notification (the newest one on screen), so one arriving meanwhile isn't marked unseen.</summary>
public sealed record NotificationMarkRequest
{
    [Range(0, long.MaxValue)]
    public long UpToId { get; init; }
}

/// <summary>Each kind: what it's called, who may get it (any one of the permissions), whether it pops up, and whether it's about a customer.</summary>
public sealed record NotificationKindInfo(NotificationKind Kind, string Label, string Description, string[] Permissions, bool Important, bool AboutCustomer);

public static class NotificationKinds
{
    public static readonly IReadOnlyList<NotificationKindInfo> All =
    [
        new(NotificationKind.PaymentReceived, L.Msg("Payments received"), L.Msg("Money received from customers."), [Security.Permissions.PaymentsView], Important: true, AboutCustomer: true),
        new(NotificationKind.OrderCreated, L.Msg("New orders"), L.Msg("Orders and quotations started by someone else."), [Security.Permissions.OrdersView], Important: false, AboutCustomer: true),
        new(NotificationKind.OrderConfirmed, L.Msg("Confirmed orders"), L.Msg("Orders confirmed and ready to invoice."), [Security.Permissions.OrdersView], Important: false, AboutCustomer: true),
        new(NotificationKind.OverdueSummary, L.Msg("Overdue invoices"), L.Msg("A morning summary of the invoices past their due date."), [Security.Permissions.InvoicesView], Important: false, AboutCustomer: false),
        new(NotificationKind.LowStock, L.Msg("Low stock"), L.Msg("A product falling to its reorder level."), [Security.Permissions.StockView], Important: false, AboutCustomer: false),
        new(NotificationKind.PurchaseOrderApproval, L.Msg("Purchase orders to approve"), L.Msg("Orders above the approval limit, waiting for someone who may approve them."), [Security.Permissions.PurchaseOrdersApprove], Important: true, AboutCustomer: false),
        new(NotificationKind.BillApproval, L.Msg("Bills to approve"), L.Msg("Supplier bills that don't match their order, waiting for approval before payment."), [Security.Permissions.SupplierBillsApprove], Important: true, AboutCustomer: false),
        new(NotificationKind.InsightsReady, L.Msg("New AI insights"), L.Msg("A fresh set of insights and forecasts."), [Security.Permissions.InsightsView], Important: false, AboutCustomer: false),
    ];

    public static NotificationKindInfo Of(NotificationKind kind) => All.Single(k => k.Kind == kind);

    /// <summary>The kinds someone may receive, from their permissions.</summary>
    public static IEnumerable<NotificationKindInfo> AllowedFor(ICurrentUser user) => All.Where(k => k.Permissions.Any(user.Has));
}
