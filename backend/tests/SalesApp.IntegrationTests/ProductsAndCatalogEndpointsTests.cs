using System.Net;
using System.Net.Http.Json;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

public sealed class ProductsAndCatalogEndpointsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Seeded starter data: category 1 = Cotton yarn, HSN 1 = 5205 at 5%.
    private static ProductSaveRequest NewProduct(string sku) => new()
    {
        Sku = sku,
        Name = "Combed cotton yarn 40s",
        CategoryId = 1,
        HsnCodeId = 1,
        Unit = ProductUnit.Kg,
        ListPrice = 312.50m,
        CostPrice = 268.00m,
        ReorderLevel = 500m,
        ReorderQuantity = 2000m,
        LeadTimeDays = 7,
    };

    private static string UniqueSku() => $"YRN-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    [Fact]
    public async Task Manager_creates_a_product_and_only_managers_see_its_cost()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var response = await manager.PostAsJsonAsync("/api/products", NewProduct(UniqueSku().ToLowerInvariant()), TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<ProductDto>();
        Assert.Equal(created.Sku.ToUpperInvariant(), created.Sku);
        Assert.Equal("5205", created.HsnCode);
        Assert.Equal(5m, created.GstRate);
        Assert.Equal(268.00m, created.CostPrice);

        var asRep = await (await rep.GetAsync($"/api/products/{created.Id}", Ct)).ReadAsync<ProductDto>();
        Assert.Null(asRep.CostPrice);
        Assert.Equal(312.50m, asRep.ListPrice);
    }

    [Fact]
    public async Task Sales_rep_cannot_create_products()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var response = await rep.PostAsJsonAsync("/api/products", NewProduct(UniqueSku()), TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_sku_is_reported_on_the_sku_field()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var sku = UniqueSku();

        (await manager.PostAsJsonAsync("/api/products", NewProduct(sku), TestClients.Json, Ct)).EnsureSuccessStatusCode();
        var duplicate = await manager.PostAsJsonAsync("/api/products", NewProduct(sku), TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("sku", (await duplicate.ReadAsync<ApiError>()).Errors!.Keys);
    }

    [Fact]
    public async Task Category_in_use_cannot_be_deleted_but_an_unused_one_can()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var category = await (await manager.PostAsJsonAsync("/api/categories",
            new CategorySaveRequest { Name = $"Temp {Guid.NewGuid():N}"[..20] }, Ct)).ReadAsync<CategoryDto>();
        (await manager.PostAsJsonAsync("/api/products", NewProduct(UniqueSku()) with { CategoryId = category.Id }, TestClients.Json, Ct))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync($"/api/categories/{category.Id}", Ct)).StatusCode);

        var unused = await (await manager.PostAsJsonAsync("/api/categories",
            new CategorySaveRequest { Name = $"Unused {Guid.NewGuid():N}"[..20] }, Ct)).ReadAsync<CategoryDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/categories/{unused.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Hsn_codes_validate_format_and_rate()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var response = await manager.PostAsJsonAsync("/api/hsn-codes",
            new HsnCodeSaveRequest { Code = "52051", Description = "Bad", GstRate = 45m }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadAsync<ApiError>()).Errors!;
        Assert.Contains("code", errors.Keys);
        Assert.Contains("gstRate", errors.Keys);
    }

    [Fact]
    public async Task States_lookup_has_every_gst_state_code()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var states = await (await rep.GetAsync("/api/lookups/states", Ct)).ReadAsync<List<StateDto>>();

        Assert.Equal(37, states.Count);
        Assert.Contains(states, s => s is { Code: "33", Name: "Tamil Nadu", UsesUtgst: false });
        Assert.Contains(states, s => s is { Code: "38", Name: "Ladakh", UsesUtgst: true });
    }

    [Fact]
    public async Task Sales_rep_lookup_is_for_managers_only()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/lookups/sales-reps", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/lookups/sales-reps", Ct)).StatusCode);
    }
}
