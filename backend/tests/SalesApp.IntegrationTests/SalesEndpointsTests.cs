using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class SalesEndpointsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string FinancialYearLabel()
    {
        var india = TimeZoneInfo.FindSystemTimeZoneById(BusinessClock.IndiaTimeZoneId);
        return FinancialYear.Label(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, india)));
    }

    private async Task<decimal> OnHandAsync(int productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockLevels
            .Where(s => s.ProductId == productId).Select(s => s.QuantityOnHand).SingleAsync(Ct);
    }

    /// <summary>Invariant: within a financial year, invoice numbers run 1, 2, 3 ... with no gaps, however tests interleave.</summary>
    private async Task AssertInvoiceNumbersAreGaplessAsync()
    {
        using var scope = factory.Services.CreateScope();
        var numbers = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices
            .Select(i => new { i.FinancialYear, i.Number }).ToListAsync(Ct);
        foreach (var year in numbers.GroupBy(n => n.FinancialYear))
        {
            var sequence = year.Select(n => int.Parse(n.Number.Split('/')[2], System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();
            Assert.Equal(Enumerable.Range(1, sequence.Count), sequence);
        }
    }

    [Fact]
    public async Task Intra_state_invoice_charges_cgst_and_sgst_and_takes_goods_out_of_stock()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 500m, listPrice: 312.50m);
        var customer = await TestData.CreateCustomerAsync(manager, "33", TestData.TamilNaduGstin);

        var order = await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 100m));
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
        Assert.False(order.IsInterState);
        Assert.Equal(32813m, order.GrandTotal); // 31250 + 781.25 + 781.25 = 32812.50 -> rounded up

        order = await TestData.ConfirmAsync(manager, order);
        var invoice = await TestData.InvoiceAsync(manager, order.Id);

        Assert.Matches($"^INV/{FinancialYearLabel()}/[0-9]{{5}}$", invoice.Number);
        Assert.Equal(31250.00m, invoice.TaxableValue);
        Assert.Equal(781.25m, invoice.CgstAmount);
        Assert.Equal(781.25m, invoice.SgstAmount);
        Assert.Equal(0m, invoice.IgstAmount);
        Assert.Equal(0.50m, invoice.RoundOff);
        Assert.Equal(32813m, invoice.GrandTotal);
        Assert.Equal("Rupees Thirty Two Thousand Eight Hundred Thirteen Only", invoice.AmountInWords);
        Assert.Equal("Tamil Nadu", invoice.PlaceOfSupplyStateName);
        Assert.Equal(TestData.TamilNaduGstin, invoice.CustomerGstin);
        Assert.Equal("33ABCDE1234F1Z7", invoice.SellerGstin);
        var hsn = Assert.Single(invoice.HsnSummary);
        Assert.Equal(("5205", 5m, 31250.00m), (hsn.HsnCode, hsn.GstRate, hsn.TaxableValue));
        Assert.Equal(invoice.InvoiceDate.AddDays(30), invoice.DueDate);

        Assert.Equal(400m, await OnHandAsync(yarn.Id));
        var ledger = await (await manager.GetAsync($"/api/stock/movements?productId={yarn.Id}", Ct)).ReadAsync<PagedResult<StockMovementDto>>();
        var sale = ledger.Items[0];
        Assert.Equal((StockMovementType.Sale, -100m, invoice.Number), (sale.Type, sale.Quantity, sale.ReferenceNumber));

        var reloaded = await (await manager.GetAsync($"/api/orders/{order.Id}", Ct)).ReadAsync<OrderDto>();
        Assert.Equal(SalesOrderStatus.Invoiced, reloaded.Status);
        Assert.Equal(invoice.Number, reloaded.InvoiceNumber);
        await AssertInvoiceNumbersAreGaplessAsync();
    }

    [Fact]
    public async Task Inter_state_invoice_charges_igst_only()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 500m, listPrice: 312.50m);
        var customer = await TestData.CreateCustomerAsync(manager, "29", TestData.KarnatakaGstin);

        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 100m)));
        var invoice = await TestData.InvoiceAsync(manager, order.Id);

        Assert.True(invoice.IsInterState);
        Assert.Equal(0m, invoice.CgstAmount + invoice.SgstAmount);
        Assert.Equal(1562.50m, invoice.IgstAmount);
        Assert.Equal(5m, Assert.Single(invoice.Lines).IgstRate);
        Assert.Equal("Karnataka", invoice.PlaceOfSupplyStateName);
    }

    [Fact]
    public async Task Not_enough_stock_aborts_the_invoice_without_using_a_number()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 10m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 25m)));

        var response = await manager.PostAsJsonAsync("/api/invoices", new InvoiceCreateRequest { SalesOrderId = order.Id }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only 10", Assert.Single((await response.ReadAsync<ApiError>()).Errors!["quantity"]), StringComparison.Ordinal);
        Assert.Equal(10m, await OnHandAsync(yarn.Id));
        Assert.Equal(SalesOrderStatus.Confirmed, (await (await manager.GetAsync($"/api/orders/{order.Id}", Ct)).ReadAsync<OrderDto>()).Status);
        await AssertInvoiceNumbersAreGaplessAsync();
    }

    [Fact]
    public async Task Concurrent_invoices_get_distinct_consecutive_numbers()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 1000m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var orders = new List<OrderDto>();
        for (var i = 0; i < 6; i++)
        {
            orders.Add(await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 5m))));
        }

        var invoices = await Task.WhenAll(orders.Select(o => TestData.InvoiceAsync(manager, o.Id)));

        Assert.Equal(6, invoices.Select(i => i.Number).Distinct().Count());
        Assert.Equal(1000m - 30m, await OnHandAsync(yarn.Id));
        await AssertInvoiceNumbersAreGaplessAsync();
    }

    [Fact]
    public async Task Payments_move_the_invoice_from_unpaid_to_partly_paid_to_paid()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 100m, listPrice: 100m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 10m)));
        var invoice = await TestData.InvoiceAsync(manager, order.Id); // 1000 + 25 + 25 = 1050

        Assert.Equal((InvoiceStatus.Unpaid, 1050m), (invoice.Status, invoice.Balance));

        (await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 400m, Mode = PaymentMode.Upi, Reference = "UPI-123" }, TestClients.Json, Ct)).EnsureSuccessStatusCode();
        var partly = await (await manager.GetAsync($"/api/invoices/{invoice.Id}", Ct)).ReadAsync<InvoiceDto>();
        Assert.Equal((InvoiceStatus.PartiallyPaid, 400m, 650m), (partly.Status, partly.AmountPaid, partly.Balance));

        var overpay = await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 650.01m, Mode = PaymentMode.Neft }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, overpay.StatusCode);
        Assert.Contains("amount", (await overpay.ReadAsync<ApiError>()).Errors!.Keys);

        (await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 650m, Mode = PaymentMode.Neft }, TestClients.Json, Ct)).EnsureSuccessStatusCode();
        var paid = await (await manager.GetAsync($"/api/invoices/{invoice.Id}", Ct)).ReadAsync<InvoiceDto>();
        Assert.Equal((InvoiceStatus.Paid, 0m), (paid.Status, paid.Balance));
        Assert.Equal(2, paid.Payments.Count);

        // A paid invoice can't be cancelled.
        var cancel = await manager.PostAsJsonAsync($"/api/invoices/{invoice.Id}/cancel", new InvoiceCancelRequest { Reason = "Test" }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    [Fact]
    public async Task Cash_receipts_of_two_lakh_or_more_in_a_day_are_refused()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 1000m, listPrice: 1000m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 300m)));
        var invoice = await TestData.InvoiceAsync(manager, order.Id);

        (await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 150000m, Mode = PaymentMode.Cash }, TestClients.Json, Ct)).EnsureSuccessStatusCode();
        var second = await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 50000m, Mode = PaymentMode.Cash }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("269ST", Assert.Single((await second.ReadAsync<ApiError>()).Errors!["amount"]), StringComparison.Ordinal);
        (await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 50000m, Mode = PaymentMode.Rtgs }, TestClients.Json, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cancelling_an_invoice_returns_stock_and_reopens_the_order_for_reinvoicing()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 50m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 20m)));
        var first = await TestData.InvoiceAsync(manager, order.Id);
        Assert.Equal(30m, await OnHandAsync(yarn.Id));

        var cancelled = await (await manager.PostAsJsonAsync($"/api/invoices/{first.Id}/cancel", new InvoiceCancelRequest { Reason = "Wrong price" }, TestClients.Json, Ct)).ReadAsync<InvoiceDto>();
        Assert.Equal(InvoiceStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, cancelled.Balance);
        Assert.Equal(50m, await OnHandAsync(yarn.Id));
        Assert.Equal(SalesOrderStatus.Confirmed, (await (await manager.GetAsync($"/api/orders/{order.Id}", Ct)).ReadAsync<OrderDto>()).Status);

        var second = await TestData.InvoiceAsync(manager, order.Id);
        Assert.NotEqual(first.Number, second.Number);
        Assert.Equal(30m, await OnHandAsync(yarn.Id));
        await AssertInvoiceNumbersAreGaplessAsync(); // the cancelled invoice keeps its number in the register
    }

    [Fact]
    public async Task Credit_limit_stops_reps_but_managers_can_override()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var yarn = await TestData.CreateStockedProductAsync(manager, 100m, listPrice: 1000m);
        var customer = await TestData.CreateCustomerAsync(manager, creditLimit: 5000m);
        var order = await TestData.CreateOrderAsync(rep, customer.Id, (yarn.Id, 10m)); // ~10,500 > 5,000

        var asRep = await rep.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new OrderConfirmRequest { RowVersion = order.RowVersion, OverrideCreditLimit = true }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, asRep.StatusCode);
        Assert.Contains("Ask someone who can override credit limits", Assert.Single((await asRep.ReadAsync<ApiError>()).Errors!["creditLimit"]), StringComparison.Ordinal);

        var managerNoOverride = await manager.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new OrderConfirmRequest { RowVersion = order.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, managerNoOverride.StatusCode);

        var confirmed = await TestData.ConfirmAsync(manager, order, overrideCreditLimit: true);
        Assert.Equal(SalesOrderStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task Order_lifecycle_rules_are_enforced()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var yarn = await TestData.CreateStockedProductAsync(manager, 100m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.CreateOrderAsync(rep, customer.Id, (yarn.Id, 5m));

        var quoted = await (await rep.PostAsJsonAsync($"/api/orders/{order.Id}/quote", new OrderActionRequest { RowVersion = order.RowVersion }, TestClients.Json, Ct)).ReadAsync<OrderDto>();
        Assert.Equal(SalesOrderStatus.Quoted, quoted.Status);
        Assert.Matches($"^QT/{FinancialYearLabel()}/[0-9]{{5}}$", quoted.QuotationNumber!);

        // Acting on a stale copy is refused.
        var stale = await rep.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new OrderConfirmRequest { RowVersion = order.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var confirmed = await TestData.ConfirmAsync(rep, quoted);
        var edit = await rep.PutAsJsonAsync($"/api/orders/{order.Id}", new OrderSaveRequest
        {
            CustomerId = customer.Id,
            Lines = [new OrderLineInput { ProductId = yarn.Id, Quantity = 1m }],
            RowVersion = confirmed.RowVersion,
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        // Reps can't issue invoices.
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PostAsJsonAsync("/api/invoices", new InvoiceCreateRequest { SalesOrderId = order.Id }, Ct)).StatusCode);

        await TestData.InvoiceAsync(manager, order.Id);
        var cancelInvoiced = await rep.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new OrderCancelRequest { Reason = "Too late", RowVersion = confirmed.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancelInvoiced.StatusCode);
    }

    [Fact]
    public async Task Invoices_cannot_be_backdated_before_the_latest_invoice_of_the_year()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateStockedProductAsync(manager, 100m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var today = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (yarn.Id, 1m)));
        await TestData.InvoiceAsync(manager, today.Id);

        var yesterday = today.OrderDate.AddDays(-1);
        var oldOrderResponse = await manager.PostAsJsonAsync("/api/orders", new OrderSaveRequest
        {
            CustomerId = customer.Id,
            OrderDate = yesterday,
            Lines = [new OrderLineInput { ProductId = yarn.Id, Quantity = 1m }],
        }, TestClients.Json, Ct);
        var oldOrder = await TestData.ConfirmAsync(manager, await oldOrderResponse.ReadAsync<OrderDto>());

        var backdated = await manager.PostAsJsonAsync("/api/invoices", new InvoiceCreateRequest { SalesOrderId = oldOrder.Id, InvoiceDate = yesterday }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, backdated.StatusCode);
        Assert.Contains("invoiceDate", (await backdated.ReadAsync<ApiError>()).Errors!.Keys);
        await AssertInvoiceNumbersAreGaplessAsync();
    }
}
