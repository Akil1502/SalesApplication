using SalesApp.Core.Security;

namespace SalesApp.Tests.Security;

public sealed class PermissionsTests
{
    [Fact]
    public void Every_menu_and_right_has_a_distinct_key()
    {
        var keys = Permissions.Menus.SelectMany(m => m.Edit is null ? [m.View] : new[] { m.View, m.Edit })
            .Concat(Permissions.SpecialRights.Select(r => r.Key)).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(keys.Count, Permissions.All.Count);
    }

    [Fact]
    public void Granting_a_permission_grants_what_it_needs()
    {
        Assert.Equal([Permissions.CustomersEdit, Permissions.CustomersView], Permissions.WithNeeds([Permissions.CustomersEdit]).Order());
        Assert.Equal(
            [Permissions.OrdersEdit, Permissions.OrdersView, Permissions.SmartEntryUse],
            Permissions.WithNeeds([Permissions.SmartEntryUse]).Order());
        Assert.Equal(
            [Permissions.InvoicesView, Permissions.PaymentsEdit, Permissions.PaymentsView],
            Permissions.WithNeeds([Permissions.PaymentsEdit]).Order());
        Assert.Empty(Permissions.WithNeeds(["not-a-permission"]));
    }

    [Fact]
    public void Taking_away_view_takes_away_what_depends_on_it()
    {
        var role = Permissions.WithNeeds([Permissions.OrdersEdit, Permissions.SmartEntryUse, Permissions.CreditLimitOverride, Permissions.ReportsView]);

        var effective = Permissions.Effective(role, granted: [], removed: [Permissions.OrdersView]);

        Assert.Equal([Permissions.ReportsView], effective);
    }

    [Fact]
    public void A_users_permissions_are_their_roles_plus_added_minus_removed()
    {
        var effective = Permissions.Effective(
            role: [Permissions.DashboardView, Permissions.ReportsView],
            granted: [Permissions.AiUsageView, Permissions.CustomersEdit],
            removed: [Permissions.DashboardView]);

        Assert.Equal(
            [Permissions.AiUsageView, Permissions.CustomersEdit, Permissions.CustomersView, Permissions.ReportsView],
            effective.Order());
    }

    [Fact]
    public void Built_in_roles_start_with_the_access_they_had_before_permissions_existed()
    {
        Assert.Equal(
            [
                "ask-data.use", "assistant.use", "customers.edit", "customers.view", "dashboard.view", "invoices.view", "orders.edit",
                "orders.view", "payments.view", "products.view", "reports.view", "sales-rep.assignable", "smart-entry.use", "stock.view",
            ],
            Roles.DefaultPermissions(Roles.SalesRep).Order(StringComparer.Ordinal));

        var manager = Roles.DefaultPermissions(Roles.SalesManager);
        Assert.Equal(Permissions.All.Count - 7, manager.Count);
        Assert.DoesNotContain(Permissions.UsersView, manager);
        Assert.DoesNotContain(Permissions.AuditView, manager);
        Assert.DoesNotContain(Permissions.RatesView, manager);
        Assert.DoesNotContain(Permissions.SettingsEdit, manager);
        Assert.DoesNotContain(Permissions.AiUsageView, manager);
        Assert.Contains(Permissions.CostPricesView, manager);

        Assert.Same(Permissions.All, Roles.DefaultPermissions(Roles.Admin));
    }

    [Fact]
    public void Every_default_set_already_holds_what_it_needs()
    {
        foreach (var role in Roles.BuiltIn)
        {
            var defaults = Roles.DefaultPermissions(role);
            Assert.True(Permissions.WithNeeds(defaults).SetEquals(defaults), role);
        }
    }
}
