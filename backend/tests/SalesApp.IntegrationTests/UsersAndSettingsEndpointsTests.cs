using System.Net;
using System.Net.Http.Json;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Auth;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

public sealed class UsersAndSettingsEndpointsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Roles.SalesRep)]
    [InlineData(Roles.SalesManager)]
    public async Task Only_admins_can_manage_users(string role)
    {
        var (client, _) = await factory.CreateClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users",
            new UserCreateRequest { Email = "x@salesapp.test", FullName = "X", Role = Roles.Admin, Password = "Whatever-123" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_creates_a_user_who_can_then_sign_in_and_admin_can_reset_their_password()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var email = $"new-rep-{Guid.NewGuid():N}@salesapp.test";

        var created = await (await admin.PostAsJsonAsync("/api/users",
            new UserCreateRequest { Email = email, FullName = "Kavya Sundar", Role = Roles.SalesRep, Password = "First-Password-1" }, Ct))
            .ReadAsync<UserDto>();
        Assert.Equal(Roles.SalesRep, created.Role);
        Assert.True(created.IsActive);

        await factory.SignInAsync(email, "First-Password-1");

        var reset = await admin.PostAsJsonAsync($"/api/users/{created.Id}/reset-password",
            new ResetPasswordRequest { NewPassword = "Second-Password-2" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "First-Password-1" }, Ct)).StatusCode);
        await factory.SignInAsync(email, "Second-Password-2");
    }

    [Fact]
    public async Task Weak_passwords_are_rejected_with_identity_reasons()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync("/api/users",
            new UserCreateRequest { Email = $"weak-{Guid.NewGuid():N}@salesapp.test", FullName = "Weak", Role = Roles.SalesRep, Password = "alllowercase" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("password", (await response.ReadAsync<ApiError>()).Errors!.Keys);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_or_demote_themselves()
    {
        var (admin, adminId) = await factory.CreateClientAsync(Roles.Admin);

        var deactivate = await admin.PutAsJsonAsync($"/api/users/{adminId}",
            new UserUpdateRequest { FullName = "Test Admin", Role = Roles.Admin, IsActive = false }, Ct);
        var demote = await admin.PutAsJsonAsync($"/api/users/{adminId}",
            new UserUpdateRequest { FullName = "Test Admin", Role = Roles.SalesRep, IsActive = true }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
    }

    [Fact]
    public async Task Changing_a_users_role_takes_effect_on_their_next_profile_read()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (rep, repId) = await factory.CreateClientAsync(Roles.SalesRep);

        (await admin.PutAsJsonAsync($"/api/users/{repId}",
            new UserUpdateRequest { FullName = "Promoted Rep", Role = Roles.SalesManager, IsActive = true }, Ct)).EnsureSuccessStatusCode();

        var me = await (await rep.GetAsync("/api/auth/me", Ct)).ReadAsync<CurrentUserDto>();
        Assert.Equal(Roles.SalesManager, me.Role);
        Assert.Contains(Permissions.CostPricesView, me.Permissions);
        Assert.Equal("Promoted Rep", me.FullName);
    }

    [Fact]
    public async Task Company_settings_are_readable_by_all_and_editable_by_admins_only()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var settings = await (await rep.GetAsync("/api/settings/company", Ct)).ReadAsync<CompanySettingsDto>();
        Assert.Equal("33ABCDE1234F1Z7", settings.Gstin);
        Assert.Equal("Tamil Nadu", settings.StateName);

        var asManager = await manager.PutAsJsonAsync("/api/settings/company", ToRequest(settings), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, asManager.StatusCode);
    }

    [Fact]
    public async Task Company_gstin_must_match_the_company_state_and_stale_edits_conflict()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var settings = await (await admin.GetAsync("/api/settings/company", Ct)).ReadAsync<CompanySettingsDto>();

        var mismatch = await admin.PutAsJsonAsync("/api/settings/company", ToRequest(settings) with { StateCode = "29" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains("gstin", (await mismatch.ReadAsync<ApiError>()).Errors!.Keys);

        var saved = await (await admin.PutAsJsonAsync("/api/settings/company", ToRequest(settings) with { Phone = "0422 2345678" }, Ct))
            .ReadAsync<CompanySettingsDto>();
        Assert.NotEqual(settings.RowVersion, saved.RowVersion);

        var stale = await admin.PutAsJsonAsync("/api/settings/company", ToRequest(settings), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    private static CompanySettingsSaveRequest ToRequest(CompanySettingsDto s) => new()
    {
        LegalName = s.LegalName,
        TradeName = s.TradeName,
        Gstin = s.Gstin,
        StateCode = s.StateCode,
        AddressLine1 = s.AddressLine1,
        AddressLine2 = s.AddressLine2,
        City = s.City,
        Pincode = s.Pincode,
        Phone = s.Phone,
        Email = s.Email,
        BankName = s.BankName,
        BankAccountNumber = s.BankAccountNumber,
        BankIfsc = s.BankIfsc,
        BankBranch = s.BankBranch,
        InvoicePrefix = s.InvoicePrefix,
        QuotationPrefix = s.QuotationPrefix,
        InvoiceTerms = s.InvoiceTerms,
        RowVersion = s.RowVersion,
    };
}
