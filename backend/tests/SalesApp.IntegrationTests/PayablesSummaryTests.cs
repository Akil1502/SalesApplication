using System.Text.Json;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>What we owe suppliers, company-wide: any other test entering or paying a bill at the same moment moves it.</summary>
[Collection(nameof(CompanyTotals))]
public sealed class PayablesSummaryTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Payables_ageing_agrees_with_the_bills_summary_and_the_purchase_register_lists_input_GST()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 10m, 250m));
        var bill = await PurchasingTests.EnterBillAsync(manager, PurchasingTests.Bill(await PurchasingTests.GetOrderAsync(manager, order.Id)));

        var summary = await (await manager.GetAsync("/api/supplier-bills/summary", Ct)).ReadAsync<SupplierBillSummaryDto>();
        var ageing = await (await manager.GetAsync("/api/reports/payables-ageing", Ct)).ReadAsync<ReportResult>();
        Assert.Equal(summary.Payable, ((JsonElement)ageing.Totals["total"]!).GetDecimal());

        var register = await (await manager.GetAsync("/api/reports/purchase-register", Ct)).ReadAsync<ReportResult>();
        var row = Assert.Single(register.Rows, r => ((JsonElement)r["number"]!).GetString() == bill.Number);
        Assert.Equal(125m, ((JsonElement)row["tax"]!).GetDecimal());

        var gst = await (await manager.GetAsync("/api/reports/gst-net", Ct)).ReadAsync<ReportResult>();
        var totals = gst.Totals;
        Assert.Equal(((JsonElement)totals["output"]!).GetDecimal() - ((JsonElement)totals["input"]!).GetDecimal(), ((JsonElement)totals["net"]!).GetDecimal());
    }
}
