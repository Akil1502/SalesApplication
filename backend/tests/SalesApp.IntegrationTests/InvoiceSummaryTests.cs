using System.Net.Http.Json;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>
/// Tests of company-wide totals. Any other test recording an invoice or a payment at the same moment moves them, so
/// these run on their own, after the tests that can run side by side.
/// </summary>
[CollectionDefinition(nameof(CompanyTotals), DisableParallelization = true)]
public sealed class CompanyTotals;

[Collection(nameof(CompanyTotals))]
public sealed class InvoiceSummaryTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Summary_reflects_outstanding_and_collected_amounts()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var before = await (await manager.GetAsync("/api/invoices/summary", Ct)).ReadAsync<InvoiceSummaryDto>();
        var yarn = await TestData.CreateStockedProductAsync(manager, 100m, listPrice: 100m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 10m)));
        var invoice = await TestData.InvoiceAsync(manager, order.Id);
        (await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 50m, Mode = PaymentMode.Upi }, TestClients.Json, Ct)).EnsureSuccessStatusCode();

        var after = await (await manager.GetAsync("/api/invoices/summary", Ct)).ReadAsync<InvoiceSummaryDto>();

        // ₹1,000 + 5% GST invoiced, ₹50 of it paid.
        Assert.Equal(1000m, after.OutstandingAmount - before.OutstandingAmount);
        Assert.Equal(50m, after.CollectedThisMonth - before.CollectedThisMonth);
    }
}
