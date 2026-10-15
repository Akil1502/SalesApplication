using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>Rate lists: rates for periods, and orders priced at the rate for their order date.</summary>
public sealed class RatesTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => factory.Services.GetRequiredService<IBusinessClock>().Today;

    private static async Task<RateBatchResult> SaveAsync(HttpClient admin, DateOnly from, DateOnly? to, params (int ProductId, decimal Rate)[] lines)
    {
        var response = await admin.PostAsJsonAsync("/api/rates/batch", new RateBatchRequest
        {
            EffectiveFrom = from,
            EffectiveTo = to,
            Note = "Test rate list",
            Lines = [.. lines.Select(l => new RateLineInput { ProductId = l.ProductId, Rate = l.Rate })],
        }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<RateBatchResult>();
    }

    private static async Task<OrderDto> OrderOnAsync(HttpClient client, int customerId, int productId, DateOnly orderDate)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new OrderSaveRequest
        {
            CustomerId = customerId,
            OrderDate = orderDate,
            Lines = [new OrderLineInput { ProductId = productId, Quantity = 1m }],
        }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<OrderDto>();
    }

    [Fact]
    public async Task Orders_are_priced_at_the_rate_for_their_order_date_and_the_list_price_outside_it()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin, listPrice: 300m);
        var customer = await TestData.CreateCustomerAsync(admin);
        var lastMonthStart = Today.AddDays(-40);
        var lastMonthEnd = Today.AddDays(-11);

        Assert.Equal(new RateBatchResult(1, 0), await SaveAsync(admin, lastMonthStart, lastMonthEnd, (product.Id, 315.50m)));

        var inPeriod = await OrderOnAsync(admin, customer.Id, product.Id, Today.AddDays(-20));
        var afterPeriod = await OrderOnAsync(admin, customer.Id, product.Id, Today);
        Assert.Equal(315.50m, Assert.Single(inPeriod.Lines).UnitPrice);
        Assert.Equal(300m, Assert.Single(afterPeriod.Lines).UnitPrice);

        // The product list prices for a chosen day, which is what the order form uses when a product is picked.
        var listed = await (await admin.GetAsync($"/api/products?q={product.Sku}&priceOn={Today.AddDays(-20):yyyy-MM-dd}", Ct)).ReadAsync<PagedResult<ProductListItemDto>>();
        var item = Assert.Single(listed.Items);
        Assert.Equal(315.50m, item.Price);
        Assert.True(item.HasRate);
        Assert.Equal(300m, item.ListPrice);

        // Changing the rate later never changes an order already priced.
        await SaveAsync(admin, lastMonthStart, lastMonthEnd, (product.Id, 999m));
        var again = await (await admin.GetAsync($"/api/orders/{inPeriod.Id}", Ct)).ReadAsync<OrderDto>();
        Assert.Equal(315.50m, Assert.Single(again.Lines).UnitPrice);
    }

    [Fact]
    public async Task Overlapping_periods_are_refused_and_the_same_period_is_updated()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin);
        var start = Today.AddDays(10);
        var end = Today.AddDays(39);

        await SaveAsync(admin, start, end, (product.Id, 310m));
        Assert.Equal(new RateBatchResult(0, 1), await SaveAsync(admin, start, end, (product.Id, 320m)));

        var overlap = await admin.PostAsJsonAsync("/api/rates/batch", new RateBatchRequest
        {
            EffectiveFrom = start.AddDays(15),
            EffectiveTo = end.AddDays(15),
            Lines = [new RateLineInput { ProductId = product.Id, Rate = 330m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        Assert.Contains(product.Sku, Assert.Single((await overlap.ReadAsync<ApiError>()).Errors!["lines"]), StringComparison.Ordinal);

        var rates = await (await admin.GetAsync($"/api/rates?productId={product.Id}", Ct)).ReadAsync<PagedResult<ProductRateDto>>();
        var only = Assert.Single(rates.Items);
        Assert.Equal(320m, only.Rate);
        Assert.Equal($"Test {Roles.Admin}", only.UpdatedBy);
    }

    [Fact]
    public async Task An_open_ended_rate_runs_until_it_is_given_an_end()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin);
        var from = Today.AddDays(5);
        await SaveAsync(admin, from, null, (product.Id, 305m));

        var blocked = await admin.PostAsJsonAsync("/api/rates/batch", new RateBatchRequest
        {
            EffectiveFrom = from.AddDays(30),
            Lines = [new RateLineInput { ProductId = product.Id, Rate = 311m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var open = Assert.Single((await (await admin.GetAsync($"/api/rates?productId={product.Id}", Ct)).ReadAsync<PagedResult<ProductRateDto>>()).Items);
        (await admin.PutAsJsonAsync($"/api/rates/{open.Id}", new RateUpdateRequest { Rate = 305m, EffectiveFrom = from, EffectiveTo = from.AddDays(29) }, TestClients.Json, Ct))
            .EnsureSuccessStatusCode();
        await SaveAsync(admin, from.AddDays(30), null, (product.Id, 311m));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/rates/{open.Id}", Ct)).StatusCode);
        Assert.Single((await (await admin.GetAsync($"/api/rates?productId={product.Id}", Ct)).ReadAsync<PagedResult<ProductRateDto>>()).Items);
    }

    [Fact]
    public async Task Rate_updates_are_for_administrators_unless_the_permission_is_given()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (rateKeeper, _) = await factory.CreateClientWithPermissionsAsync(Permissions.RatesEdit);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/rates", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/rates/excel", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rateKeeper.GetAsync("/api/rates", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_downloaded_rate_sheet_filled_in_and_uploaded_comes_back_as_rows_to_review()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(admin, listPrice: 300m);

        var download = await admin.GetAsync($"/api/rates/excel?q={product.Sku}", Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        using var filled = new MemoryStream();
        using (var workbook = new XLWorkbook(await download.Content.ReadAsStreamAsync(Ct)))
        {
            var sheet = workbook.Worksheets.First();
            var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == product.Sku);
            Assert.Equal(300d, row.Cell(5).GetDouble());
            row.Cell(6).Value = 312.75;
            var unknown = row.RowBelow();
            unknown.Cell(1).Value = "NO-SUCH-SKU";
            unknown.Cell(6).Value = 100;
            workbook.SaveAs(filled);
        }

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(filled.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "rates.xlsx");
        var imported = await (await admin.PostAsync("/api/rates/import", form, Ct)).ReadAsync<RateImportResult>();

        var line = Assert.Single(imported.Lines);
        Assert.Equal((product.Id, 312.75m), (line.ProductId, line.Rate));
        Assert.Contains(imported.Problems, p => p.Contains("NO-SUCH-SKU", StringComparison.Ordinal));
        Assert.Empty((await (await admin.GetAsync($"/api/rates?productId={product.Id}", Ct)).ReadAsync<PagedResult<ProductRateDto>>()).Items);
    }

    [Fact]
    public async Task Something_that_isnt_a_workbook_is_refused()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        using var form = new MultipartFormDataContent { { new ByteArrayContent("SKU,New rate\nX,1"u8.ToArray()), "file", "rates.csv" } };

        var response = await admin.PostAsync("/api/rates/import", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
