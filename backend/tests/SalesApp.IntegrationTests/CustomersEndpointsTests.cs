using System.Net;
using System.Net.Http.Json;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

public sealed class CustomersEndpointsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CustomerSaveRequest NewCustomer(string name, string? gstin = null, string stateCode = "33") => new()
    {
        Name = name,
        Gstin = gstin,
        Phone = "+91 98400 12345",
        Email = "purchase@example.in",
        BillingAddress = new AddressInput { Line1 = "4 Avinashi Road", City = "Tiruppur", StateCode = stateCode, Pincode = "641601" },
        CreditLimit = 500000m,
        PaymentTermsDays = 45,
    };

    [Fact]
    public async Task Manager_creates_a_customer_with_a_generated_code_and_derived_pan()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var response = await client.PostAsJsonAsync("/api/customers", NewCustomer("Sri Murugan Knits", gstin: "33abcde1234f1z7"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = await response.ReadAsync<CustomerDto>();
        Assert.Matches("^C[0-9]{5}$", customer.Code);
        Assert.Equal("33ABCDE1234F1Z7", customer.Gstin);
        Assert.Equal("ABCDE1234F", customer.Pan);
        Assert.Equal("Tamil Nadu", customer.BillingAddress.StateName);
        Assert.Equal("+919840012345", customer.Phone);
        Assert.Null(customer.ShippingAddress);
        Assert.Equal(500000m, customer.CreditLimit);
    }

    [Fact]
    public async Task Invalid_gstin_is_reported_on_the_gstin_field()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var response = await client.PostAsJsonAsync("/api/customers", NewCustomer("Typo Traders", gstin: "33ABCDE1234F1Z8"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadAsync<ApiError>();
        Assert.Contains("check character", Assert.Single(error.Errors!["gstin"]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gstin_must_belong_to_the_billing_state()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesManager);

        // Karnataka GSTIN with a Tamil Nadu billing address.
        var response = await client.PostAsJsonAsync("/api/customers", NewCustomer("Bengaluru Fabrics", gstin: "29AAGCB7383J1Z4"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadAsync<ApiError>();
        Assert.Contains("Karnataka", Assert.Single(error.Errors!["gstin"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_searches_and_pages()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var marker = $"Paging{Guid.NewGuid():N}"[..14];
        foreach (var suffix in new[] { "A", "B", "C" })
        {
            (await client.PostAsJsonAsync("/api/customers", NewCustomer($"{marker} {suffix}"), Ct)).EnsureSuccessStatusCode();
        }

        var page1 = await (await client.GetAsync($"/api/customers?q={marker}&pageSize=2&sort=-name", Ct)).ReadAsync<PagedResult<CustomerListItemDto>>();
        var page2 = await (await client.GetAsync($"/api/customers?q={marker}&pageSize=2&page=2&sort=-name", Ct)).ReadAsync<PagedResult<CustomerListItemDto>>();

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal([$"{marker} C", $"{marker} B"], page1.Items.Select(c => c.Name));
        Assert.Equal($"{marker} A", Assert.Single(page2.Items).Name);
    }

    [Fact]
    public async Task Unknown_sort_column_is_a_validation_error()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var response = await client.GetAsync("/api/customers?sort=password", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sort", (await response.ReadAsync<ApiError>()).Errors!.Keys);
    }

    [Fact]
    public async Task Stale_row_version_returns_409()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var created = await (await client.PostAsJsonAsync("/api/customers", NewCustomer("Concurrent Mills"), Ct)).ReadAsync<CustomerDto>();

        var first = await client.PutAsJsonAsync($"/api/customers/{created.Id}", NewCustomer("Concurrent Mills Ltd") with { RowVersion = created.RowVersion }, Ct);
        var second = await client.PutAsJsonAsync($"/api/customers/{created.Id}", NewCustomer("Concurrent Mills Pvt") with { RowVersion = created.RowVersion }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Sales_rep_owns_their_new_customers_and_cannot_set_credit_terms()
    {
        var (rep, repId) = await factory.CreateClientAsync(Roles.SalesRep);

        var created = await (await rep.PostAsJsonAsync("/api/customers", NewCustomer("Rep's Customer"), Ct)).ReadAsync<CustomerDto>();
        Assert.Equal(repId, created.AssignedRepId);
        Assert.Null(created.CreditLimit);
        Assert.Equal(30, created.PaymentTermsDays);

        var updated = await (await rep.PutAsJsonAsync($"/api/customers/{created.Id}",
            NewCustomer("Rep's Customer") with { CreditLimit = 9_999_999m, PaymentTermsDays = 120, RowVersion = created.RowVersion }, Ct))
            .ReadAsync<CustomerDto>();
        Assert.Null(updated.CreditLimit);
        Assert.Equal(30, updated.PaymentTermsDays);
    }

    [Fact]
    public async Task Custom_codes_must_be_unique()
    {
        var (client, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var code = $"K{Random.Shared.Next(100000, 999999)}";

        (await client.PostAsJsonAsync("/api/customers", NewCustomer("First") with { Code = code }, Ct)).EnsureSuccessStatusCode();
        var duplicate = await client.PostAsJsonAsync("/api/customers", NewCustomer("Second") with { Code = code.ToLowerInvariant() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("code", (await duplicate.ReadAsync<ApiError>()).Errors!.Keys);
    }
}
