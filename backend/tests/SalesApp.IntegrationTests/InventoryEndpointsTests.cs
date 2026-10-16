using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class InventoryEndpointsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StockAdjustmentRequest Adjust(int productId, decimal quantity, AdjustmentMode mode = AdjustmentMode.Change,
        AdjustmentReason reason = AdjustmentReason.Damaged) =>
        new() { ProductId = productId, Quantity = quantity, Mode = mode, Reason = reason, Notes = "Integration test" };

    private async Task<decimal> OnHandAsync(int productId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockLevels.Where(s => s.ProductId == productId).Select(s => s.QuantityOnHand).SingleAsync(Ct);
    }

    private async Task<decimal> LedgerSumAsync(int productId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockMovements.Where(m => m.ProductId == productId).SumAsync(m => m.Quantity, Ct);
    }

    [Fact]
    public async Task Receipt_adds_stock_and_records_ledger_lines()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager);
        var fabric = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (yarn.Id, 1250.5m, 262m), (fabric.Id, 40m, 90m)));

        var response = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            SupplierReference = "LSM/2026/0412",
            Lines = [.. order.Lines.Select(l => new GoodsReceiptLineInput { PurchaseOrderLineId = l.Id, Quantity = l.Quantity })],
        }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.ReadAsync<StockReceiptDto>();
        Assert.Matches("^GRN-[0-9]{5}$", receipt.Number);
        Assert.Equal(order.Number, receipt.PurchaseOrderNumber);
        Assert.Equal(supplier.Name, receipt.SupplierName);
        Assert.Equal(2, receipt.Lines.Count);
        Assert.Equal(1250.5m, await OnHandAsync(yarn.Id));

        var product = await (await manager.GetAsync($"/api/products/{yarn.Id}", Ct)).ReadAsync<ProductDto>();
        Assert.Equal(1250.5m, product.QuantityOnHand);

        var ledger = await (await manager.GetAsync($"/api/stock/movements?productId={yarn.Id}", Ct)).ReadAsync<PagedResult<StockMovementDto>>();
        var line = Assert.Single(ledger.Items);
        Assert.Equal(StockMovementType.Receipt, line.Type);
        Assert.Equal(receipt.Number, line.ReferenceNumber);
        Assert.Equal(1250.5m, line.BalanceAfter);
        Assert.Equal(262m, line.UnitCost);
    }

    [Fact]
    public async Task On_hand_always_equals_the_sum_of_the_ledger()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);

        (await manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, 300m, reason: AdjustmentReason.OpeningBalance), TestClients.Json, Ct)).EnsureSuccessStatusCode();
        await TestData.BuyAsync(manager, (product.Id, 500m, 250m));
        (await manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, -12.25m), TestClients.Json, Ct)).EnsureSuccessStatusCode();
        await TestData.BuyAsync(manager, (product.Id, 75.125m, 255m));
        (await manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, 850m, AdjustmentMode.SetCount, AdjustmentReason.StockCount), TestClients.Json, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(850m, await OnHandAsync(product.Id));
        Assert.Equal(await OnHandAsync(product.Id), await LedgerSumAsync(product.Id));

        var ledger = await (await manager.GetAsync($"/api/stock/movements?productId={product.Id}", Ct)).ReadAsync<PagedResult<StockMovementDto>>();
        Assert.Equal([StockMovementType.Adjustment, StockMovementType.Receipt, StockMovementType.Adjustment, StockMovementType.Receipt, StockMovementType.Opening],
            ledger.Items.Select(m => m.Type));
        Assert.Equal(850m - 862.875m, ledger.Items[0].Quantity); // the count's difference
    }

    [Fact]
    public async Task Removing_more_than_is_on_hand_is_refused_and_changes_nothing()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        await TestData.AddOpeningStockAsync(manager, product.Id, 10m);

        var response = await manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, -10.5m), TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only 10", Assert.Single((await response.ReadAsync<ApiError>()).Errors!["quantity"]), StringComparison.Ordinal);
        Assert.Equal(10m, await OnHandAsync(product.Id));
        Assert.Equal(10m, await LedgerSumAsync(product.Id));
    }

    [Fact]
    public async Task Three_decimal_quantities_are_kept_exactly_and_more_decimals_are_rejected()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);

        // Regression: raw-SQL decimal parameters used to default to scale 2 and store 75.13.
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 100m, 250.5m)));
        await TestData.ReceiveAsync(manager, order, 75.125m);
        Assert.Equal(75.125m, await OnHandAsync(product.Id));

        order = await (await manager.GetAsync($"/api/purchase-orders/{order.Id}", Ct)).ReadAsync<PurchaseOrderDto>();
        var tooPrecise = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 1.2345m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
        Assert.Contains("lines[0].quantity", (await tooPrecise.ReadAsync<ApiError>()).Errors!.Keys);
        Assert.Equal(75.125m, await OnHandAsync(product.Id));
    }

    [Fact]
    public async Task Concurrent_postings_never_lose_an_update()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, 1m, reason: AdjustmentReason.Found), TestClients.Json, Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(20m, await OnHandAsync(product.Id));

        // Every posting saw a distinct running balance: 1, 2, ... 20.
        var ledger = await (await manager.GetAsync($"/api/stock/movements?productId={product.Id}&pageSize=50", Ct)).ReadAsync<PagedResult<StockMovementDto>>();
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (decimal)i), ledger.Items.Select(m => m.BalanceAfter).Order());
    }

    [Fact]
    public async Task Stock_count_that_matches_is_rejected_and_opening_balance_is_one_time_only()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        await TestData.BuyAsync(manager, (product.Id, 5m, 250m));

        var sameCount = await manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, 5m, AdjustmentMode.SetCount, AdjustmentReason.StockCount), TestClients.Json, Ct);
        var lateOpening = await manager.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, 100m, reason: AdjustmentReason.OpeningBalance), TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, sameCount.StatusCode);
        Assert.Contains("quantity", (await sameCount.ReadAsync<ApiError>()).Errors!.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, lateOpening.StatusCode);
        Assert.Contains("reason", (await lateOpening.ReadAsync<ApiError>()).Errors!.Keys);
    }

    [Fact]
    public async Task Receipts_reject_future_dates_and_a_line_listed_twice()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 10m, 250m)));
        var line = order.Lines[0].Id;

        var future = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            ReceiptDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3),
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = line, Quantity = 5m }],
        }, TestClients.Json, Ct);
        var duplicate = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = line, Quantity = 5m }, new GoodsReceiptLineInput { PurchaseOrderLineId = line, Quantity = 2m }],
        }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("receiptDate", (await future.ReadAsync<ApiError>()).Errors!.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("lines[1].purchaseOrderLineId", (await duplicate.ReadAsync<ApiError>()).Errors!.Keys);
        Assert.Equal(0m, await OnHandAsync(product.Id));
    }

    [Fact]
    public async Task Goods_can_only_come_in_against_a_purchase_order()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var response = await manager.PostAsJsonAsync("/api/stock/receipts", new { lines = new[] { new { productId = 1, quantity = 5 } } }, TestClients.Json, Ct);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task Sales_reps_can_view_stock_but_not_change_it_or_see_its_value()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var product = await TestData.CreateProductAsync(manager, reorderLevel: 50m, costPrice: 200m);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 20m, 200m));

        var asRep = await (await rep.GetAsync($"/api/stock/levels?q={product.Sku}", Ct)).ReadAsync<PagedResult<StockLevelDto>>();
        var asManager = await (await manager.GetAsync($"/api/stock/levels?q={product.Sku}", Ct)).ReadAsync<PagedResult<StockLevelDto>>();

        var repRow = Assert.Single(asRep.Items);
        Assert.Equal(StockStatus.Low, repRow.Status);
        Assert.Null(repRow.StockValue);
        Assert.Equal(4000m, Assert.Single(asManager.Items).StockValue);
        Assert.Null((await (await rep.GetAsync("/api/stock/summary", Ct)).ReadAsync<StockSummaryDto>()).TotalStockValue);

        var receive = new GoodsReceiptCreateRequest { RowVersion = order.RowVersion, Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 1m }] };
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", receive, TestClients.Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PostAsJsonAsync("/api/stock/adjustments", Adjust(product.Id, 1m), TestClients.Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Status_filter_finds_low_and_out_of_stock_products()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var empty = await TestData.CreateProductAsync(manager, reorderLevel: 10m);
        var low = await TestData.CreateProductAsync(manager, reorderLevel: 10m);
        var healthy = await TestData.CreateProductAsync(manager, reorderLevel: 10m);
        await TestData.AddOpeningStockAsync(manager, low.Id, 4m);
        await TestData.AddOpeningStockAsync(manager, healthy.Id, 40m);

        async Task<HashSet<int>> IdsWith(StockStatus status) =>
            [.. (await (await manager.GetAsync($"/api/stock/levels?status={status}&pageSize=100&q=T-", Ct)).ReadAsync<PagedResult<StockLevelDto>>())
                .Items.Select(i => i.ProductId)];

        var outOfStock = await IdsWith(StockStatus.OutOfStock);
        var lowOrOut = await IdsWith(StockStatus.Low);
        var inStock = await IdsWith(StockStatus.InStock);

        Assert.Contains(empty.Id, outOfStock);
        Assert.DoesNotContain(low.Id, outOfStock);
        Assert.Contains(low.Id, lowOrOut);
        Assert.Contains(empty.Id, lowOrOut);
        Assert.Contains(healthy.Id, inStock);
        Assert.DoesNotContain(low.Id, inStock);
    }
}
