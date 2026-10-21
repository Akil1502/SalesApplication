using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Notifications;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Notifications;

namespace SalesApp.IntegrationTests;

/// <summary>
/// Notifications: who hears about what (permissions, their own customers, never their own actions), read marks,
/// muting, low stock only on the way down, and the morning overdue summary.
/// </summary>
public sealed class NotificationsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<NotificationListDto> ListAsync(HttpClient client, bool unreadOnly = false) =>
        await (await client.GetAsync($"/api/notifications?take=100&unreadOnly={unreadOnly}", Ct)).ReadAsync<NotificationListDto>();

    /// <summary>
    /// Someone who hears only about the one customer they look after (no "every customer" right, nothing else that
    /// other tests could disturb), plus that customer, created and assigned by a manager.
    /// </summary>
    private async Task<(HttpClient Client, Guid UserId, CustomerDto Customer)> WatcherAsync(HttpClient manager, params string[] permissions)
    {
        var (client, userId) = await factory.CreateClientWithPermissionsAsync([.. permissions, Permissions.SalesRepAssignable]);
        var response = await manager.PostAsJsonAsync("/api/customers", new CustomerSaveRequest
        {
            Name = $"Watched {Guid.NewGuid():N}"[..20],
            Gstin = TestData.TamilNaduGstin,
            BillingAddress = new AddressInput { Line1 = "1 Market Street", City = "Test City", StateCode = "33", Pincode = "641601" },
            AssignedRepId = userId,
        }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        return (client, userId, await response.ReadAsync<CustomerDto>());
    }

    private static async Task<InvoiceDto> InvoicedOrderAsync(HttpClient manager, int customerId)
    {
        var product = await TestData.CreateStockedProductAsync(manager, 50m);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customerId, (product.Id, 2m)));
        return await TestData.InvoiceAsync(manager, order.Id);
    }

    private static async Task PayAsync(HttpClient client, InvoiceDto invoice, decimal amount) =>
        (await client.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = amount, Mode = PaymentMode.Upi }, TestClients.Json, Ct))
        .EnsureSuccessStatusCode();

    [Fact]
    public async Task A_payment_reaches_the_customers_rep_and_managers_but_not_whoever_recorded_it_or_other_reps()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (otherManager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var (otherRep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var customer = await TestData.CreateCustomerAsync(rep); // a rep's new customer is theirs
        var invoice = await InvoicedOrderAsync(manager, customer.Id);
        await PayAsync(manager, invoice, 100m);

        bool Heard(NotificationListDto list) => list.Items.Any(n => n.Kind == NotificationKind.PaymentReceived && n.Link == $"/invoices/{invoice.Id}");
        var forRep = (await ListAsync(rep)).Items.Single(n => n.Kind == NotificationKind.PaymentReceived && n.Link == $"/invoices/{invoice.Id}");
        Assert.Equal($"₹100 received from {customer.Name}", forRep.Title);
        Assert.Equal($"UPI against {invoice.Number}", forRep.Body);
        Assert.True(forRep.Important);
        Assert.False(forRep.Read);
        Assert.True(Heard(await ListAsync(otherManager)));
        Assert.False(Heard(await ListAsync(manager)));
        Assert.False(Heard(await ListAsync(otherRep)));
    }

    [Fact]
    public async Task Read_marks_and_since_your_last_visit_are_kept_per_person()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (otherManager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (watcher, _, customer) = await WatcherAsync(manager, Permissions.OrdersView, Permissions.PaymentsView);
        Assert.Equal(0, (await ListAsync(watcher)).UnreadCount);

        var invoice = await InvoicedOrderAsync(manager, customer.Id); // a new order, then confirmed
        await PayAsync(manager, invoice, 50m);
        var list = await ListAsync(watcher);
        Assert.Equal(
            [NotificationKind.PaymentReceived, NotificationKind.OrderConfirmed, NotificationKind.OrderCreated],
            list.Items.Select(n => n.Kind));
        Assert.Equal(3, list.UnreadCount);

        Assert.Equal(HttpStatusCode.NoContent, (await watcher.PostAsync($"/api/notifications/{list.Items[1].Id}/read", null, Ct)).StatusCode);
        var afterOne = await ListAsync(watcher);
        Assert.Equal(2, afterOne.UnreadCount);
        Assert.True(afterOne.Items[1].Read);
        Assert.Equal(2, (await ListAsync(watcher, unreadOnly: true)).Items.Count);

        var newest = list.Items[0].Id;
        await watcher.PostAsJsonAsync("/api/notifications/opened", new NotificationMarkRequest { UpToId = newest }, Ct);
        await watcher.PostAsJsonAsync("/api/notifications/read-all", new NotificationMarkRequest { UpToId = newest }, Ct);
        var afterAll = await ListAsync(watcher);
        Assert.Equal(0, afterAll.UnreadCount);
        Assert.Equal(newest, afterAll.LastOpenedUpToId);

        // One arriving afterwards is unread and newer than the last visit.
        await PayAsync(manager, invoice, 25m);
        var later = await ListAsync(watcher);
        Assert.Equal(1, later.UnreadCount);
        Assert.True(later.Items[0].Id > later.LastOpenedUpToId);

        // Someone else's marks are their own.
        Assert.False((await ListAsync(otherManager)).Items.Single(n => n.Id == list.Items[1].Id).Read);
    }

    [Fact]
    public async Task Muted_kinds_disappear_and_only_kinds_someone_may_get_are_offered()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (watcher, _, customer) = await WatcherAsync(manager, Permissions.OrdersView, Permissions.PaymentsView);

        var offered = await (await watcher.GetAsync("/api/notifications/preferences", Ct)).ReadAsync<List<NotificationKindDto>>();
        Assert.Equal([NotificationKind.PaymentReceived, NotificationKind.OrderCreated, NotificationKind.OrderConfirmed], offered.Select(k => k.Kind));
        Assert.All(offered, k => Assert.False(k.Muted));

        var saved = await (await watcher.PutAsJsonAsync("/api/notifications/preferences",
            new NotificationPreferencesRequest { Muted = [NotificationKind.OrderCreated, NotificationKind.OrderConfirmed] }, TestClients.Json, Ct))
            .ReadAsync<List<NotificationKindDto>>();
        Assert.Equal([false, true, true], saved.Select(k => k.Muted));

        var invoice = await InvoicedOrderAsync(manager, customer.Id);
        await PayAsync(manager, invoice, 10m);
        var list = await ListAsync(watcher);
        Assert.Equal([NotificationKind.PaymentReceived], list.Items.Select(n => n.Kind));
        Assert.Equal(1, list.UnreadCount);
    }

    [Fact]
    public async Task Purchase_orders_above_the_limit_reach_only_people_who_may_approve_them()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (approver, _) = await factory.CreateClientWithPermissionsAsync(Permissions.PurchaseOrdersApprove);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 1000m, 600m)));
        Assert.Equal(PurchaseOrderStatus.PendingApproval, order.Status);

        var heard = (await ListAsync(approver)).Items.Single(n => n.Link == $"/purchase-orders/{order.Id}");
        Assert.Equal(NotificationKind.PurchaseOrderApproval, heard.Kind);
        Assert.Equal($"Purchase order {order.Number} needs your approval", heard.Title);
        Assert.DoesNotContain((await ListAsync(manager)).Items, n => n.Link == $"/purchase-orders/{order.Id}");
    }

    [Fact]
    public async Task Low_stock_is_reported_on_the_way_down_and_again_only_after_restocking()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (storekeeper, _) = await factory.CreateClientWithPermissionsAsync(Permissions.StockView);
        var product = await TestData.CreateProductAsync(admin, reorderLevel: 10m);

        var opened = false;
        async Task AdjustAsync(decimal quantity)
        {
            (await admin.PostAsJsonAsync("/api/stock/adjustments", new StockAdjustmentRequest
            {
                ProductId = product.Id,
                Quantity = quantity,
                Mode = AdjustmentMode.Change,
                Reason = quantity < 0 ? AdjustmentReason.Damaged : opened ? AdjustmentReason.Found : AdjustmentReason.OpeningBalance,
                Notes = "Notification test",
            }, TestClients.Json, Ct)).EnsureSuccessStatusCode();
            opened = true;
        }

        async Task<List<NotificationDto>> HeardAsync() => [.. (await ListAsync(storekeeper)).Items.Where(n => n.Link == $"/products/{product.Id}")];

        await AdjustAsync(20m);
        await AdjustAsync(-15m); // 20 → 5: falls to the reorder level
        var first = Assert.Single(await HeardAsync());
        Assert.Equal($"{product.Name} is low on stock", first.Title);
        Assert.StartsWith("5 ", first.Body, StringComparison.Ordinal);

        await AdjustAsync(-2m); // still low: nothing new
        Assert.Single(await HeardAsync());

        await AdjustAsync(30m); // restocked to 33
        await AdjustAsync(-33m); // and out again
        var again = await HeardAsync();
        Assert.Equal(2, again.Count);
        Assert.Equal($"{product.Name} is out of stock", again[0].Title);
    }

    [Fact]
    public async Task The_morning_summary_counts_each_persons_overdue_invoices_once_a_day()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (watcher, watcherId, customer) = await WatcherAsync(manager, Permissions.InvoicesView);
        var (otherWatcher, _, _) = await WatcherAsync(manager, Permissions.InvoicesView);
        var invoice = await InvoicedOrderAsync(manager, customer.Id);
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400); // long after every test invoice is due

        async Task<int> RunAsync()
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().ActAs(SystemUser.Instance);
            return await scope.ServiceProvider.GetRequiredService<INotificationDigest>().RunAsync(day, Ct);
        }

        Assert.True(await RunAsync() > 0);
        var summary = Assert.Single((await ListAsync(watcher)).Items, n => n.Kind == NotificationKind.OverdueSummary);
        Assert.Equal("1 invoice is overdue", summary.Title);
        Assert.Contains(invoice.Number, summary.Body, StringComparison.Ordinal);
        Assert.Equal("/invoices?view=overdue", summary.Link);

        // Only to them: it's about their customers (the other watcher's customer owes nothing).
        Assert.DoesNotContain((await ListAsync(otherWatcher)).Items, n => n.Id == summary.Id);

        await RunAsync();
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.RecipientUserId == watcherId && n.Kind == NotificationKind.OverdueSummary, Ct));
    }

    [Fact]
    public async Task Times_are_sent_as_UTC_so_browsers_show_them_in_their_own_time_zone()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (watcher, _, customer) = await WatcherAsync(manager, Permissions.OrdersView);
        await TestData.CreateOrderAsync(manager, customer.Id, ((await TestData.CreateStockedProductAsync(manager, 5m)).Id, 1m));

        using var json = System.Text.Json.JsonDocument.Parse(await (await watcher.GetAsync("/api/notifications", Ct)).Content.ReadAsStringAsync(Ct));
        var createdAt = json.RootElement.GetProperty("items")[0].GetProperty("createdAtUtc").GetString()!;
        Assert.EndsWith("Z", createdAt, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.Parse(createdAt, System.Globalization.CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Notifications_need_signing_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications", Ct)).StatusCode);
    }
}
