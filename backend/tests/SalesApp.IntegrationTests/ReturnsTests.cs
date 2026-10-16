using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Contracts.Returns;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class ReturnsTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => factory.Services.GetRequiredService<IBusinessClock>().Today;

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<decimal> OnHandAsync(int productId) => WithDbAsync(db => db.StockLevels.Where(s => s.ProductId == productId).Select(s => s.QuantityOnHand).SingleAsync(Ct));

    private Task<decimal?> CostAsync(int productId) => WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.CostPrice).SingleAsync(Ct));

    /// <summary>A stocked product (₹300 a kg, 5% GST) sold to a new Tamil Nadu customer: CGST + SGST.</summary>
    private async Task<(HttpClient Manager, InvoiceDto Invoice, ProductDto Product)> SellAsync(decimal stock, decimal quantity)
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateStockedProductAsync(manager, stock);
        var customer = await TestData.CreateCustomerAsync(manager);
        return (manager, await InvoiceForAsync(manager, customer.Id, product.Id, quantity), product);
    }

    private static async Task<InvoiceDto> InvoiceForAsync(HttpClient manager, int customerId, int productId, decimal quantity)
    {
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customerId, (productId, quantity)));
        return await TestData.InvoiceAsync(manager, order.Id);
    }

    private static CreditNoteCreateRequest Goods(InvoiceDto invoice, decimal quantity, ReturnDisposition disposition = ReturnDisposition.Restock) => new()
    {
        InvoiceId = invoice.Id,
        Kind = CreditNoteKind.GoodsReturn,
        Reason = disposition == ReturnDisposition.Damaged ? ReturnReason.Damaged : ReturnReason.QualityIssue,
        Lines = [new CreditNoteLineInput { InvoiceLineId = invoice.Lines[0].Id, Quantity = quantity, Disposition = disposition }],
    };

    private static async Task<CreditNoteDto> IssueAsync(HttpClient client, CreditNoteCreateRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/credit-notes", request, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<CreditNoteDto>();
    }

    private static async Task<InvoiceDto> GetInvoiceAsync(HttpClient client, int id) =>
        await (await client.GetAsync($"/api/invoices/{id}", Ct)).ReadAsync<InvoiceDto>();

    private static async Task<CreditNoteDto> GetCreditNoteAsync(HttpClient client, int id) =>
        await (await client.GetAsync($"/api/credit-notes/{id}", Ct)).ReadAsync<CreditNoteDto>();

    private static async Task PayAsync(HttpClient client, int invoiceId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoiceId, Amount = amount, Mode = PaymentMode.Neft }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static decimal Number(Dictionary<string, object?> row, string key) => ((JsonElement)row[key]!).GetDecimal();

    private static string? Text(Dictionary<string, object?> row, string key) => row[key] is JsonElement { ValueKind: JsonValueKind.String } e ? e.GetString() : null;

    // ---- Credit notes ---------------------------------------------------------------------------

    [Fact]
    public async Task Goods_returned_go_back_into_stock_and_come_off_what_is_due()
    {
        var (manager, invoice, product) = await SellAsync(100m, 40m);
        Assert.Equal(12_600m, invoice.GrandTotal); // 40 × ₹300 + 5% GST
        Assert.Null(invoice.CancelBlockedReason);

        var note = await IssueAsync(manager, Goods(invoice, 10m));

        Assert.Matches("^CN/[0-9]{2}-[0-9]{2}/[0-9]{5}$", note.Number);
        Assert.True(note.WithGst);
        Assert.Equal(3_000m, note.TaxableValue);
        Assert.Equal(75m, note.CgstAmount);
        Assert.Equal(75m, note.SgstAmount);
        Assert.Equal(3_150m, note.GrandTotal);
        Assert.Equal(3_150m, note.AmountAdjusted);
        Assert.Equal(0m, note.CreditAvailable);
        Assert.Equal(invoice.Number, Assert.Single(note.Adjustments).DocumentNumber);
        Assert.Equal(70m, await OnHandAsync(product.Id));

        var after = await GetInvoiceAsync(manager, invoice.Id);
        Assert.Equal(3_150m, after.AmountCredited);
        Assert.Equal(9_450m, after.Balance);
        Assert.Equal(InvoiceStatus.PartiallyPaid, after.Status);
        Assert.Equal(10m, after.Lines[0].QuantityReturned);
        Assert.Equal(3_000m, after.Lines[0].ValueCredited);
        Assert.Equal(note.Number, Assert.Single(after.CreditNotes).Number);
        Assert.Contains("credit notes", after.CancelBlockedReason, StringComparison.Ordinal);

        // A payment can now only cover what's left.
        var tooMuch = await manager.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 9_451m, Mode = PaymentMode.Neft }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        await PayAsync(manager, invoice.Id, 9_450m);
        Assert.Equal(InvoiceStatus.Paid, (await GetInvoiceAsync(manager, invoice.Id)).Status);
    }

    [Fact]
    public async Task Damaged_goods_are_recorded_as_returned_and_written_off()
    {
        var (manager, invoice, product) = await SellAsync(50m, 10m);
        Task<int> AdjustmentEntriesAsync() => WithDbAsync(db => db.AuditEntries.CountAsync(e => e.EntityType == "Stock adjustment" && e.EntityLabel == product.Sku, Ct));
        var before = await AdjustmentEntriesAsync(); // the opening stock

        var note = await IssueAsync(manager, Goods(invoice, 4m, ReturnDisposition.Damaged));

        Assert.Equal(40m, await OnHandAsync(product.Id));
        var movements = await WithDbAsync(db => db.StockMovements.Where(m => m.ReferenceType == StockReferenceTypes.CreditNote && m.ReferenceId == note.Id)
            .OrderBy(m => m.Id).Select(m => new { m.Type, m.Quantity, m.Reason }).ToListAsync(Ct));
        Assert.Collection(movements,
            m => Assert.Equal((StockMovementType.SaleReturn, 4m), (m.Type, m.Quantity)),
            m => Assert.Equal((StockMovementType.Adjustment, -4m, (AdjustmentReason?)AdjustmentReason.Damaged), (m.Type, m.Quantity, m.Reason)));

        // The write-off is covered by the credit note itself, not logged as a hand-made stock adjustment.
        Assert.Equal(before, await AdjustmentEntriesAsync());
    }

    [Fact]
    public async Task A_line_returned_in_pieces_adds_up_exactly_and_no_more_can_come_back()
    {
        var (manager, invoice, _) = await SellAsync(20m, 7m);

        var first = await IssueAsync(manager, Goods(invoice, 3m));
        var second = await IssueAsync(manager, Goods(invoice, 4m));

        Assert.Equal(invoice.TaxableValue, first.TaxableValue + second.TaxableValue);
        Assert.Equal(invoice.GrandTotal, first.GrandTotal + second.GrandTotal);
        var after = await GetInvoiceAsync(manager, invoice.Id);
        Assert.Equal(0m, after.Balance);
        Assert.Equal(InvoiceStatus.Paid, after.Status);

        var again = await manager.PostAsJsonAsync("/api/credit-notes", Goods(invoice, 1m), TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var error = await again.ReadAsync<ApiError>();
        Assert.Contains("fully credited", Assert.Single(error.Errors!["lines[0].quantity"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task More_than_was_invoiced_can_not_be_returned()
    {
        var (manager, invoice, _) = await SellAsync(20m, 5m);
        await IssueAsync(manager, Goods(invoice, 2m));

        var response = await manager.PostAsJsonAsync("/api/credit-notes", Goods(invoice, 4m), TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only 3 Kg can still be returned", Assert.Single((await response.ReadAsync<ApiError>()).Errors!["lines[0].quantity"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_price_correction_moves_no_goods_and_lowers_what_later_returns_are_worth()
    {
        var (manager, invoice, product) = await SellAsync(30m, 10m);
        var correction = new CreditNoteCreateRequest
        {
            InvoiceId = invoice.Id,
            Kind = CreditNoteKind.PriceCorrection,
            Reason = ReturnReason.RateDifference,
            ReasonNote = "Agreed ₹250 a kg",
            Lines = [new CreditNoteLineInput { InvoiceLineId = invoice.Lines[0].Id, Amount = 500m }],
        };

        var preview = await (await manager.PostAsJsonAsync("/api/credit-notes/preview", correction, TestClients.Json, Ct)).ReadAsync<CreditNotePreviewDto>();
        Assert.Equal(525m, preview.GrandTotal);
        Assert.Equal(525m, preview.OffInvoice);
        Assert.Equal(0m, preview.LeftAsCredit);

        var note = await IssueAsync(manager, correction);
        Assert.Equal(525m, note.GrandTotal);
        Assert.Null(Assert.Single(note.Lines).Quantity);
        Assert.Equal(20m, await OnHandAsync(product.Id));

        // ₹2,500 of value left over 10 kg: each kg returned now credits ₹250.
        var returned = await IssueAsync(manager, Goods(invoice, 2m));
        Assert.Equal(500m, returned.TaxableValue);

        var tooMuch = correction with { Lines = [new CreditNoteLineInput { InvoiceLineId = invoice.Lines[0].Id, Amount = 2_000.01m }] };
        var response = await manager.PostAsJsonAsync("/api/credit-notes", tooMuch, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Credit_beyond_what_is_due_stays_with_the_customer_to_use_or_refund()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateStockedProductAsync(manager, 100m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var paid = await InvoiceForAsync(manager, customer.Id, product.Id, 10m);
        await PayAsync(manager, paid.Id, paid.GrandTotal);

        var note = await IssueAsync(manager, Goods(paid, 4m));
        Assert.Equal(0m, note.AmountAdjusted);
        Assert.Equal(1_260m, note.CreditAvailable);
        Assert.Equal(1_260m, (await GetInvoiceAsync(manager, paid.Id)).CustomerCreditAvailable);

        var available = await (await manager.GetAsync($"/api/credit-notes/available?customerId={customer.Id}", Ct)).ReadAsync<List<AvailableCreditDto>>();
        Assert.Equal(1_260m, Assert.Single(available).Available);

        // Set ₹1,000 against their next invoice...
        var next = await InvoiceForAsync(manager, customer.Id, product.Id, 10m);
        var applied = await (await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/adjustments",
            new CreditApplyRequest { DocumentId = next.Id, Amount = 1_000m, RowVersion = note.RowVersion }, TestClients.Json, Ct)).ReadAsync<CreditNoteDto>();
        Assert.Equal(260m, applied.CreditAvailable);
        Assert.Equal(next.GrandTotal - 1_000m, (await GetInvoiceAsync(manager, next.Id)).Balance);

        // ...not against someone else's...
        var (_, stranger, _) = await SellAsync(10m, 1m);
        var wrong = await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/adjustments",
            new CreditApplyRequest { DocumentId = stranger.Id, Amount = 10m, RowVersion = applied.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        // ...and refund the rest.
        var refunded = await (await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/refunds",
            new RefundCreateRequest { Amount = 260m, Mode = PaymentMode.Neft, Reference = "UTR123", RowVersion = applied.RowVersion }, TestClients.Json, Ct)).ReadAsync<CreditNoteDto>();
        Assert.Equal(0m, refunded.CreditAvailable);
        Assert.Equal(260m, Assert.Single(refunded.Refunds).Amount);

        var more = await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/refunds",
            new RefundCreateRequest { Amount = 1m, Mode = PaymentMode.Cash, RowVersion = refunded.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, more.StatusCode);

        // Used elsewhere and refunded: it can no longer be cancelled.
        Assert.Contains("Refunds are recorded", refunded.CancelBlockedReason, StringComparison.Ordinal);
        var cancel = await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/cancel", new ReturnCancelRequest { Reason = "Mistake", RowVersion = refunded.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    [Fact]
    public async Task Cancelling_a_credit_note_puts_the_goods_and_the_balance_back()
    {
        var (manager, invoice, product) = await SellAsync(50m, 10m);
        var note = await IssueAsync(manager, Goods(invoice, 4m));
        Assert.Equal(44m, await OnHandAsync(product.Id));

        var response = await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/cancel",
            new ReturnCancelRequest { Reason = "Customer kept the goods after all", RowVersion = note.RowVersion }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        var cancelled = await response.ReadAsync<CreditNoteDto>();

        Assert.Equal(ReturnDocumentStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, cancelled.CreditAvailable);
        Assert.True(Assert.Single(cancelled.Adjustments).Reversed);
        Assert.Equal(40m, await OnHandAsync(product.Id));
        var after = await GetInvoiceAsync(manager, invoice.Id);
        Assert.Equal(0m, after.AmountCredited);
        Assert.Equal(InvoiceStatus.Unpaid, after.Status);
        Assert.Null(after.CancelBlockedReason);

        var again = await manager.PostAsJsonAsync($"/api/credit-notes/{note.Id}/cancel",
            new ReturnCancelRequest { Reason = "Again", RowVersion = cancelled.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task After_the_GST_time_limit_a_credit_note_credits_the_value_only()
    {
        var (manager, invoice, _) = await SellAsync(20m, 10m);
        // As if it had been invoiced two years ago: 30 November after its financial year has long gone.
        var longAgo = Today.AddYears(-2);
        await WithDbAsync(db => db.Invoices.Where(i => i.Id == invoice.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.InvoiceDate, longAgo).SetProperty(i => i.DueDate, longAgo), Ct));

        var preview = await (await manager.PostAsJsonAsync("/api/credit-notes/preview", Goods(invoice, 2m), TestClients.Json, Ct)).ReadAsync<CreditNotePreviewDto>();
        Assert.False(preview.WithGst);
        Assert.True(preview.GstDeadline < Today);

        var note = await IssueAsync(manager, Goods(invoice, 2m));
        Assert.False(note.WithGst);
        Assert.Equal(600m, note.TaxableValue);
        Assert.Equal(0m, note.CgstAmount + note.SgstAmount + note.IgstAmount);
        Assert.Equal(600m, note.GrandTotal);

        // The value comes off sales (nothing else for this customer today), but GST reports leave the line out.
        var sales = await (await manager.GetAsync($"/api/reports/sales-by-customer?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct)).ReadAsync<ReportResult>();
        Assert.Equal(-600m, Number(Assert.Single(sales.Rows, r => Text(r, "customer") == invoice.CustomerName), "taxable"));
        var line = await WithDbAsync(db => db.RptSalesLines.SingleAsync(l => l.DocumentNumber == note.Number, Ct));
        Assert.Equal(("CreditNote", false, -2m), (line.DocumentType, line.AffectsGst, line.Quantity));
    }

    [Fact]
    public async Task An_invoice_can_be_cancelled_only_while_unpaid_uncredited_and_in_its_own_month()
    {
        var (manager, paid, product) = await SellAsync(50m, 2m);
        await PayAsync(manager, paid.Id, 100m);
        var paidNow = await GetInvoiceAsync(manager, paid.Id);
        Assert.Contains("payments recorded", paidNow.CancelBlockedReason, StringComparison.Ordinal);

        var customer = await TestData.CreateCustomerAsync(manager);
        var lastMonth = await InvoiceForAsync(manager, customer.Id, product.Id, 1m);
        var monthEnd = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);
        await WithDbAsync(db => db.Invoices.Where(i => i.Id == lastMonth.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.InvoiceDate, monthEnd), Ct));

        var reloaded = await GetInvoiceAsync(manager, lastMonth.Id);
        Assert.Contains("GST return (GSTR-1)", reloaded.CancelBlockedReason, StringComparison.Ordinal);
        var cancel = await manager.PostAsJsonAsync($"/api/invoices/{lastMonth.Id}/cancel", new InvoiceCancelRequest { Reason = "Wrong customer" }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Contains("Issue a credit note instead", (await cancel.ReadAsync<ApiError>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sales_reports_and_receivables_are_net_of_credit_notes()
    {
        var (manager, invoice, _) = await SellAsync(50m, 10m);
        await PayAsync(manager, invoice.Id, invoice.GrandTotal);
        var note = await IssueAsync(manager, Goods(invoice, 4m)); // ₹1,200 + GST, all left as credit

        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}";
        var byCustomer = await (await manager.GetAsync($"/api/reports/sales-by-customer?{range}", Ct)).ReadAsync<ReportResult>();
        var row = Assert.Single(byCustomer.Rows, r => Text(r, "customer") == invoice.CustomerName);
        Assert.Equal(1_800m, Number(row, "taxable"));
        Assert.Equal(1m, Number(row, "invoices"));

        var gst = await (await manager.GetAsync($"/api/reports/gst-summary?{range}", Ct)).ReadAsync<ReportResult>();
        Assert.Contains(gst.Rows, r => Text(r, "document") == "Credit notes" && Number(r, "taxable") <= -1_200m);

        var ageing = await (await manager.GetAsync("/api/reports/receivables-ageing", Ct)).ReadAsync<ReportResult>();
        var customer = Assert.Single(ageing.Rows, r => Text(r, "customer") == invoice.CustomerName);
        Assert.Equal(note.GrandTotal, Number(customer, "credit"));
        Assert.Equal(-note.GrandTotal, Number(customer, "net"));

        var register = await (await manager.GetAsync($"/api/reports/credit-notes?{range}", Ct)).ReadAsync<ReportResult>();
        Assert.Equal(note.GrandTotal, Number(Assert.Single(register.Rows, r => Text(r, "number") == note.Number), "unused"));
    }

    [Fact]
    public async Task Sales_reps_can_see_credit_notes_but_not_issue_them()
    {
        var (_, invoice, _) = await SellAsync(10m, 2m);
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        Assert.Equal(HttpStatusCode.OK, (await rep.GetAsync("/api/credit-notes", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PostAsJsonAsync("/api/credit-notes", Goods(invoice, 1m), TestClients.Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/supplier-returns", Ct)).StatusCode);
    }

    // ---- Supplier returns -----------------------------------------------------------------------

    private static async Task<SupplierBillDto> BillAsync(HttpClient client, PurchaseOrderDto order)
    {
        var current = await (await client.GetAsync($"/api/purchase-orders/{order.Id}", Ct)).ReadAsync<PurchaseOrderDto>();
        var response = await client.PostAsJsonAsync("/api/supplier-bills", new SupplierBillCreateRequest
        {
            PurchaseOrderId = order.Id,
            SupplierInvoiceNumber = $"INV-{Guid.NewGuid():N}"[..14],
            Lines = [.. current.Lines.Select(l => new SupplierBillLineInput { PurchaseOrderLineId = l.Id, Quantity = l.QuantityReceived, UnitPrice = l.UnitPrice })],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SupplierBillDto>();
    }

    private static SupplierReturnCreateRequest ReturnOf(SupplierBillDto bill, decimal quantity) => new()
    {
        SupplierBillId = bill.Id,
        Reason = ReturnReason.QualityIssue,
        ReasonNote = "Uneven dyeing",
        Lines = [new SupplierReturnLineInput { SupplierBillLineId = bill.Lines[0].Id, Quantity = quantity }],
    };

    private static async Task<SupplierReturnDto> ReturnAsync(HttpClient client, SupplierReturnCreateRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/supplier-returns", request, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SupplierReturnDto>();
    }

    [Fact]
    public async Task Goods_returned_to_a_supplier_leave_at_the_bill_price_and_come_off_what_we_owe()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var first = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 100m, 200m)));
        await TestData.ReceiveAsync(manager, first, 100m);
        var second = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 50m, 230m)));
        await TestData.ReceiveAsync(manager, second, 50m);
        Assert.Equal(210m, await CostAsync(product.Id));
        var bill = await BillAsync(manager, second); // 50 kg × ₹230 + 5% = ₹12,075

        var ret = await ReturnAsync(manager, ReturnOf(bill, 50m));

        Assert.Matches("^DN/[0-9]{2}-[0-9]{2}/[0-9]{5}$", ret.Number);
        Assert.Equal(11_500m, ret.TaxableValue);
        Assert.Equal(12_075m, ret.GrandTotal);
        Assert.Equal(12_075m, ret.AmountAdjusted);
        Assert.Null(ret.SupplierCreditNoteNumber);
        Assert.Equal(100m, await OnHandAsync(product.Id));
        Assert.Equal(200m, await CostAsync(product.Id)); // what's left cost ₹200 a kg

        var after = await (await manager.GetAsync($"/api/supplier-bills/{bill.Id}", Ct)).ReadAsync<SupplierBillDto>();
        Assert.Equal(0m, after.Balance);
        Assert.Equal(SupplierBillStatus.Paid, after.Status);
        Assert.Equal(50m, after.Lines[0].QuantityReturned);
        Assert.Contains("returned", after.CancelBlockedReason, StringComparison.Ordinal);

        // Noting the supplier's credit note when it arrives.
        var noted = await (await manager.PutAsJsonAsync($"/api/supplier-returns/{ret.Id}/supplier-credit-note",
            new SupplierReturnNoteRequest { SupplierCreditNoteNumber = "SCN-77", SupplierCreditNoteDate = Today, RowVersion = ret.RowVersion }, TestClients.Json, Ct)).ReadAsync<SupplierReturnDto>();
        Assert.Equal("SCN-77", noted.SupplierCreditNoteNumber);

        // Input GST comes back off this month's credit.
        var gst = await (await manager.GetAsync($"/api/reports/supplier-returns?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct)).ReadAsync<ReportResult>();
        var row = Assert.Single(gst.Rows, r => Text(r, "number") == ret.Number);
        Assert.Equal("SCN-77", Text(row, "theirNote"));

        // Cancelling brings the goods and the cost back.
        var cancelled = await (await manager.PostAsJsonAsync($"/api/supplier-returns/{ret.Id}/cancel",
            new ReturnCancelRequest { Reason = "Supplier sent a replacement instead", RowVersion = noted.RowVersion }, TestClients.Json, Ct)).ReadAsync<SupplierReturnDto>();
        Assert.Equal(ReturnDocumentStatus.Cancelled, cancelled.Status);
        Assert.Equal(150m, await OnHandAsync(product.Id));
        Assert.Equal(210m, await CostAsync(product.Id));
        Assert.Equal(12_075m, (await (await manager.GetAsync($"/api/supplier-bills/{bill.Id}", Ct)).ReadAsync<SupplierBillDto>()).Balance);
    }

    [Fact]
    public async Task A_return_on_a_paid_bill_is_credit_with_the_supplier()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 20m, 100m)));
        await TestData.ReceiveAsync(manager, order, 20m);
        var bill = await BillAsync(manager, order); // ₹2,100
        var pay = await manager.PostAsJsonAsync("/api/supplier-payments", new SupplierPaymentCreateRequest { SupplierBillId = bill.Id, Amount = 2_100m, Mode = PaymentMode.Neft }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, pay.StatusCode);

        var ret = await ReturnAsync(manager, ReturnOf(bill, 5m));
        Assert.Equal(525m, ret.CreditAvailable);

        var tooMany = await manager.PostAsJsonAsync("/api/supplier-returns", ReturnOf(bill, 16m), TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var refunded = await (await manager.PostAsJsonAsync($"/api/supplier-returns/{ret.Id}/refunds",
            new RefundCreateRequest { Amount = 525m, Mode = PaymentMode.Neft, RowVersion = ret.RowVersion }, TestClients.Json, Ct)).ReadAsync<SupplierReturnDto>();
        Assert.Equal(0m, refunded.CreditAvailable);

        var ageing = await (await manager.GetAsync("/api/reports/payables-ageing", Ct)).ReadAsync<ReportResult>();
        Assert.DoesNotContain(ageing.Rows, r => Text(r, "supplier") == supplier.Name);
    }

    [Fact]
    public async Task Goods_rejected_on_delivery_never_enter_stock_and_the_order_still_expects_them()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 100m, 200m)));

        var noReason = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 60m, QuantityRejected = 40m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Contains("lines[0].rejectionReason", (await noReason.ReadAsync<ApiError>()).Errors!.Keys);

        var response = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 60m, QuantityRejected = 40m, RejectionReason = "Wet cones" }],
        }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        var receipt = await response.ReadAsync<StockReceiptDto>();

        var line = Assert.Single(receipt.Lines);
        Assert.Equal((60m, 40m, "Wet cones"), (line.Quantity, line.QuantityRejected, line.RejectionReason));
        Assert.Equal(60m, await OnHandAsync(product.Id));
        var after = await (await manager.GetAsync($"/api/purchase-orders/{order.Id}", Ct)).ReadAsync<PurchaseOrderDto>();
        Assert.Equal(PurchaseOrderStatus.PartlyReceived, after.Status);
        Assert.Equal(60m, after.Lines[0].QuantityReceived);

        // Everything in the next delivery rejected: a receipt on record, nothing in stock.
        var allRejected = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = after.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 0m, QuantityRejected = 40m, RejectionReason = "Wrong count" }],
        }, TestClients.Json, Ct);
        allRejected.EnsureSuccessStatusCode();
        Assert.Equal(60m, await OnHandAsync(product.Id));
    }
}
