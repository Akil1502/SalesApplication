using System.Net;
using System.Net.Http.Json;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>Roles as permission sets, per-user changes, and the API enforcing them on every request.</summary>
public sealed class PermissionsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_with_only_ai_usage_opens_that_page_and_nothing_else()
    {
        var (client, _) = await factory.CreateClientWithPermissionsAsync(Permissions.AiUsageView);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/ai-usage/summary", Ct)).StatusCode);
        foreach (var path in new[] { "/api/dashboard", "/api/orders", "/api/invoices", "/api/customers", "/api/reports", "/api/users", "/api/insights" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path, Ct)).StatusCode);
        }

        var me = await (await client.GetAsync("/api/auth/me", Ct)).ReadAsync<CurrentUserDto>();
        Assert.Equal([Permissions.AiUsageView], me.Permissions);
    }

    [Fact]
    public async Task Permissions_given_to_or_taken_from_one_user_apply_on_their_next_request()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (rep, repId) = await factory.CreateClientAsync(Roles.SalesRep);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/admin/ai-usage/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rep.GetAsync("/api/dashboard", Ct)).StatusCode);

        var updated = await (await admin.PutAsJsonAsync($"/api/users/{repId}", new UserUpdateRequest
        {
            FullName = "Rep With Extras",
            Role = Roles.SalesRep,
            AddedPermissions = [Permissions.AiUsageView],
            RemovedPermissions = [Permissions.DashboardView],
        }, Ct)).ReadAsync<UserDto>();
        Assert.Equal([Permissions.AiUsageView], updated.AddedPermissions);
        Assert.Equal([Permissions.DashboardView], updated.RemovedPermissions);

        // Same token, no new sign-in.
        Assert.Equal(HttpStatusCode.OK, (await rep.GetAsync("/api/admin/ai-usage/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/dashboard", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rep.GetAsync("/api/orders", Ct)).StatusCode);
    }

    [Fact]
    public async Task Taking_away_view_also_takes_away_add_and_change()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (rep, repId) = await factory.CreateClientAsync(Roles.SalesRep);

        (await admin.PutAsJsonAsync($"/api/users/{repId}", new UserUpdateRequest
        {
            FullName = "Rep Without Customers",
            Role = Roles.SalesRep,
            RemovedPermissions = [Permissions.CustomersView],
        }, Ct)).EnsureSuccessStatusCode();

        var me = await (await rep.GetAsync("/api/auth/me", Ct)).ReadAsync<CurrentUserDto>();
        Assert.DoesNotContain(Permissions.CustomersView, me.Permissions);
        Assert.DoesNotContain(Permissions.CustomersEdit, me.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PostAsJsonAsync("/api/customers", new { name = "Blocked" }, Ct)).StatusCode);

        // Pickers still work for someone who can enter orders, without the Customers menu.
        Assert.Equal(HttpStatusCode.OK, (await rep.GetAsync("/api/customers?pageSize=5", Ct)).StatusCode);
    }

    [Fact]
    public async Task Changing_a_role_changes_access_for_everyone_in_it_at_once()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var role = await (await admin.PostAsJsonAsync("/api/roles",
            new RoleSaveRequest { Name = $"Accounts {Guid.NewGuid():N}", Description = "Invoices and payments", Permissions = [Permissions.PaymentsEdit] }, Ct))
            .ReadAsync<RoleDto>();
        // Recording payments needs the payments and invoices views, so the role gets them too.
        Assert.Equal([Permissions.InvoicesView, Permissions.PaymentsEdit, Permissions.PaymentsView], role.Permissions);

        var (accountant, _) = await factory.CreateClientAsync(role.Name);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/payments", Ct)).StatusCode);

        (await admin.PutAsJsonAsync($"/api/roles/{role.Id}",
            new RoleSaveRequest { Name = role.Name, Permissions = [Permissions.ReportsView] }, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/payments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/reports", Ct)).StatusCode);
    }

    [Fact]
    public async Task Built_in_roles_are_protected_and_a_role_in_use_cant_be_deleted()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var roles = await (await admin.GetAsync("/api/roles", Ct)).ReadAsync<List<RoleDto>>();
        var administrator = Assert.Single(roles, r => r.IsSystem);
        Assert.Equal(Roles.Admin, administrator.Name);
        Assert.Equal(Permissions.All.Count, administrator.Permissions.Count);
        var rep = Assert.Single(roles, r => r.Name == Roles.SalesRep);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/roles/{administrator.Id}",
            new RoleSaveRequest { Name = Roles.Admin, Permissions = [] }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/roles/{rep.Id}",
            new RoleSaveRequest { Name = "Field sales", Permissions = rep.Permissions }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/roles/{rep.Id}", Ct)).StatusCode);

        var custom = await (await admin.PostAsJsonAsync("/api/roles",
            new RoleSaveRequest { Name = $"Temp {Guid.NewGuid():N}", Permissions = [Permissions.ReportsView] }, Ct)).ReadAsync<RoleDto>();
        var (_, userId) = await factory.CreateClientAsync(custom.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/roles/{custom.Id}", Ct)).StatusCode);

        (await admin.PutAsJsonAsync($"/api/users/{userId}", new UserUpdateRequest { FullName = "Moved", Role = Roles.SalesRep }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{custom.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task User_admins_cant_change_their_own_permissions_or_lock_themselves_out()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var role = await (await admin.PostAsJsonAsync("/api/roles",
            new RoleSaveRequest { Name = $"User admins {Guid.NewGuid():N}", Permissions = [Permissions.UsersEdit] }, Ct)).ReadAsync<RoleDto>();
        var (userAdmin, userAdminId) = await factory.CreateClientAsync(role.Name);

        var selfGrant = await userAdmin.PutAsJsonAsync($"/api/users/{userAdminId}", new UserUpdateRequest
        {
            FullName = "Self Promoter",
            Role = role.Name,
            AddedPermissions = [Permissions.SettingsEdit],
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, selfGrant.StatusCode);

        var lockout = await userAdmin.PutAsJsonAsync($"/api/roles/{role.Id}",
            new RoleSaveRequest { Name = role.Name, Permissions = [Permissions.UsersView] }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, lockout.StatusCode);
    }

    [Fact]
    public async Task Only_users_who_can_be_a_sales_rep_are_offered_as_one()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (_, plannerId) = await factory.CreateClientWithPermissionsAsync(Permissions.ReportsView);
        var (_, fieldId) = await factory.CreateClientWithPermissionsAsync(Permissions.SalesRepAssignable, Permissions.OrdersEdit);
        var (_, adminId) = await factory.CreateClientAsync(Roles.Admin);

        var reps = await (await manager.GetAsync("/api/lookups/sales-reps", Ct)).ReadAsync<List<UserLookupDto>>();

        Assert.Contains(reps, r => r.Id == fieldId);
        Assert.DoesNotContain(reps, r => r.Id == plannerId);
        Assert.DoesNotContain(reps, r => r.Id == adminId);
    }

    [Fact]
    public async Task A_deactivated_users_token_is_refused_everywhere()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (rep, repId) = await factory.CreateClientAsync(Roles.SalesRep);

        (await admin.PutAsJsonAsync($"/api/users/{repId}", new UserUpdateRequest { FullName = "Gone", Role = Roles.SalesRep, IsActive = false }, Ct))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/orders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await rep.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_permission_list_follows_the_sidebar()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);

        var catalog = await (await admin.GetAsync("/api/roles/permissions", Ct)).ReadAsync<PermissionCatalogDto>();

        Assert.Equal("Dashboard", catalog.Menus[0].Name);
        Assert.Contains(catalog.Menus, m => m.Name == "AI usage" && m.View == Permissions.AiUsageView && m.Edit is null);
        Assert.Contains(catalog.Menus, m => m.Name == "Invoices" && m.Edit == Permissions.InvoicesEdit);
        Assert.Contains(catalog.SpecialRights, r => r.Key == Permissions.CostPricesView);
    }
}
