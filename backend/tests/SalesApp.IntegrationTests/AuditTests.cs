using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

/// <summary>The audit trail: field changes with old and new values, security events, and that entries can't be altered.</summary>
public sealed class AuditTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CustomerSaveRequest Customer(string name, decimal creditLimit, int terms, string? rowVersion = null) => new()
    {
        Name = name,
        Phone = "+91 98400 12345",
        BillingAddress = new AddressInput { Line1 = "4 Avinashi Road", City = "Tiruppur", StateCode = "33", Pincode = "641601" },
        CreditLimit = creditLimit,
        PaymentTermsDays = terms,
        RowVersion = rowVersion,
    };

    private static async Task<List<AuditEntryDto>> HistoryAsync(HttpClient admin, string query) =>
        [.. (await (await admin.GetAsync($"/api/audit?pageSize=100&{query}", Ct)).ReadAsync<PagedResult<AuditEntryDto>>()).Items];

    [Fact]
    public async Task A_change_is_recorded_field_by_field_with_who_made_it()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var name = $"Audit Knits {Guid.NewGuid():N}"[..24];
        var created = await (await manager.PostAsJsonAsync("/api/customers", Customer(name, 500000m, 30), Ct)).ReadAsync<CustomerDto>();

        var moved = Customer(name, 750000m, 45, created.RowVersion) with
        {
            BillingAddress = new AddressInput { Line1 = "4 Avinashi Road", City = "Coimbatore", StateCode = "33", Pincode = "641018" },
        };
        (await manager.PutAsJsonAsync($"/api/customers/{created.Id}", moved, Ct)).EnsureSuccessStatusCode();

        var history = await HistoryAsync(admin, $"entityType=Customer&entityId={created.Id}");
        Assert.Equal([AuditAction.Updated, AuditAction.Created], history.Select(h => h.Action));
        var update = history[0];
        Assert.Equal($"Test {Roles.SalesManager}", update.UserName);
        Assert.Equal(name, update.EntityLabel);
        Assert.Contains(new AuditChange("Credit limit", "₹5,00,000", "₹7,50,000"), update.Changes);
        Assert.Contains(new AuditChange("Payment terms (days)", "30", "45"), update.Changes);
        Assert.Contains(new AuditChange("Billing address: city", "Tiruppur", "Coimbatore"), update.Changes);
        Assert.Contains(new AuditChange("Billing address: PIN code", "641601", "641018"), update.Changes);
        Assert.DoesNotContain(update.Changes, c => c.Field == "Billing address: line 1");
    }

    [Fact]
    public async Task A_refused_change_leaves_no_entry()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var name = $"Stale Mills {Guid.NewGuid():N}"[..24];
        var created = await (await admin.PostAsJsonAsync("/api/customers", Customer(name, 100000m, 30), Ct)).ReadAsync<CustomerDto>();
        (await admin.PutAsJsonAsync($"/api/customers/{created.Id}", Customer(name, 200000m, 30, created.RowVersion), Ct)).EnsureSuccessStatusCode();

        var stale = await admin.PutAsJsonAsync($"/api/customers/{created.Id}", Customer(name, 900000m, 30, created.RowVersion), Ct);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var updates = (await HistoryAsync(admin, $"entityType=Customer&entityId={created.Id}")).Where(h => h.Action == AuditAction.Updated).ToList();
        Assert.Single(updates);
        Assert.DoesNotContain(updates, u => u.Changes.Any(c => c.New == "₹9,00,000"));
    }

    [Fact]
    public async Task An_orders_lines_are_shown_before_and_after()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin, listPrice: 300m);
        var customer = await TestData.CreateCustomerAsync(admin);
        var order = await TestData.CreateOrderAsync(admin, customer.Id, (product.Id, 500m));

        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}", new OrderSaveRequest
        {
            CustomerId = customer.Id,
            OrderDate = order.OrderDate,
            RowVersion = order.RowVersion,
            Lines = [new OrderLineInput { ProductId = product.Id, Quantity = 650m, UnitPrice = 290m }],
        }, TestClients.Json, Ct)).EnsureSuccessStatusCode();

        var history = await HistoryAsync(admin, $"entityType=Order&entityId={order.Id}");
        var created = Assert.Single(history, h => h.Action == AuditAction.Created);
        Assert.Contains(created.Changes, c => c.Field == "Lines" && c.Old == null && c.New == $"{product.Sku} × 500 @ ₹300");
        Assert.Contains(created.Changes, c => c.Field == "Customer" && c.New == customer.Name);
        var edited = Assert.Single(history, h => h.Action == AuditAction.Updated);
        Assert.Contains(new AuditChange("Lines", $"{product.Sku} × 500 @ ₹300", $"{product.Sku} × 650 @ ₹290"), edited.Changes);
    }

    [Fact]
    public async Task Sign_ins_failed_sign_ins_and_downloads_are_recorded()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (manager, managerId) = await factory.CreateClientAsync(Roles.SalesManager);
        var unknown = $"nobody-{Guid.NewGuid():N}@salesapp.test";
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = unknown, Password = "Wrong-Password-1" }, Ct);
        (await manager.GetAsync("/api/reports/gst-summary/excel", Ct)).EnsureSuccessStatusCode();

        var failed = Assert.Single(await HistoryAsync(admin, $"action=SignInFailed&q={unknown}"));
        Assert.Null(failed.UserId);
        Assert.Equal("No account with this email.", failed.Summary);
        Assert.Contains(await HistoryAsync(admin, $"action=SignedIn&userId={managerId}"), e => e.EntityId == managerId.ToString());
        var export = Assert.Single(await HistoryAsync(admin, $"action=Exported&userId={managerId}"));
        Assert.Contains("GST summary", export.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Permission_changes_name_the_permissions()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (_, repId) = await factory.CreateClientAsync(Roles.SalesRep);

        (await admin.PutAsJsonAsync($"/api/users/{repId}", new UserUpdateRequest
        {
            FullName = "Audited Rep",
            Role = Roles.SalesManager,
            RemovedPermissions = [Permissions.InsightsView],
        }, Ct)).EnsureSuccessStatusCode();

        var history = await HistoryAsync(admin, $"entityType=User&entityId={repId}");
        var access = Assert.Single(history, h => h.Action == AuditAction.PermissionsChanged);
        Assert.Contains(new AuditChange("Role", Roles.SalesRep, Roles.SalesManager), access.Changes);
        Assert.Contains(new AuditChange("Taken away for this user", null, "AI insights"), access.Changes);
        Assert.Contains(history, h => h.Action == AuditAction.Updated && h.Changes.Contains(new AuditChange("Full name", $"Test {Roles.SalesRep}", "Audited Rep")));
    }

    [Fact]
    public async Task The_audit_log_is_for_administrators_unless_the_permission_is_given()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (auditor, _) = await factory.CreateClientWithPermissionsAsync(Permissions.AuditView);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/audit", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/api/audit", Ct)).StatusCode);
    }

    [Fact]
    public async Task Entries_cannot_be_changed_or_removed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditEntries.Add(new AuditEntry { UserName = "Test", Action = AuditAction.Exported, Summary = "Tamper test", OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);

        var update = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE AuditEntries SET Summary = 'Changed' WHERE Summary = 'Tamper test'", Ct));
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM AuditEntries WHERE Summary = 'Tamper test'", Ct));
        Assert.Contains("append-only", update.Message, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bulk_set_up_work_can_run_without_filling_the_trail()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Quiet {Guid.NewGuid():N}"[..20];
        using (AuditInterceptor.Suppress())
        {
            db.Categories.Add(new Category { Name = name });
            await db.SaveChangesAsync(Ct);
        }

        Assert.False(await db.AuditEntries.AnyAsync(a => a.EntityLabel == name, Ct));
    }
}
