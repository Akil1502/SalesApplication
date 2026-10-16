using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class PurchasingTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<decimal?> CostAsync(int productId) => WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.CostPrice).SingleAsync(Ct));

    private Task<decimal> OnHandAsync(int productId) => WithDbAsync(db => db.StockLevels.Where(s => s.ProductId == productId).Select(s => s.QuantityOnHand).SingleAsync(Ct));

    private static async Task<PurchaseOrderDto> GetOrderAsync(HttpClient client, int id) =>
        await (await client.GetAsync($"/api/purchase-orders/{id}", Ct)).ReadAsync<PurchaseOrderDto>();

    private static SupplierBillCreateRequest Bill(PurchaseOrderDto order, decimal? price = null, decimal? quantity = null, string? invoice = null) => new()
    {
        PurchaseOrderId = order.Id,
        SupplierInvoiceNumber = invoice ?? $"INV-{Guid.NewGuid():N}"[..14],
        Lines = [.. order.Lines.Select(l => new SupplierBillLineInput { PurchaseOrderLineId = l.Id, Quantity = quantity ?? l.QuantityReceived, UnitPrice = price ?? l.UnitPrice })],
    };

    private static async Task<SupplierBillDto> EnterBillAsync(HttpClient client, SupplierBillCreateRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/supplier-bills", request, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SupplierBillDto>();
    }

    // ---- Suppliers ------------------------------------------------------------------------------

    [Fact]
    public async Task Suppliers_get_a_code_and_their_GSTIN_must_match_their_state()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var supplier = await TestData.CreateSupplierAsync(manager, stateCode: "29", gstin: TestData.KarnatakaGstin);
        Assert.Matches("^S[0-9]{5}$", supplier.Code);
        Assert.True(supplier.IsInterState); // the company is in Tamil Nadu
        Assert.Equal("AAGCB7383J", supplier.Pan);

        var mismatch = await manager.PostAsJsonAsync("/api/suppliers", new SupplierSaveRequest
        {
            Name = "Wrong State Mills",
            Gstin = TestData.KarnatakaGstin,
            Address = new AddressInput { Line1 = "1 Road", City = "Erode", StateCode = "33", Pincode = "638001" },
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains("gstin", (await mismatch.ReadAsync<ApiError>()).Errors!.Keys);
    }

    [Fact]
    public async Task Sales_reps_have_no_access_to_purchasing()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        foreach (var url in new[] { "/api/suppliers", "/api/purchase-orders", "/api/supplier-bills", "/api/supplier-payments" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync(url, Ct)).StatusCode);
        }

        var reports = await (await rep.GetAsync("/api/reports", Ct)).ReadAsync<List<ReportDefinitionDto>>();
        Assert.DoesNotContain(reports, r => r.Key == "payables-ageing");
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/reports/payables-ageing", Ct)).StatusCode);
    }

    // ---- Orders ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_order_is_drafted_edited_and_placed_with_prices_from_the_last_purchase()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager, costPrice: 240m);
        var supplier = await TestData.CreateSupplierAsync(manager);

        // No history with this supplier: the price starts at the product's cost.
        var draft = await manager.PostAsJsonAsync("/api/purchase-orders", new PurchaseOrderSaveRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseOrderLineInput { ProductId = product.Id, Quantity = 100m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var order = await draft.ReadAsync<PurchaseOrderDto>();
        Assert.Matches(@"^PO/\d{2}-\d{2}/\d{5}$", order.Number);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal(240m, order.Lines[0].UnitPrice);
        Assert.Equal(25_200m, order.GrandTotal); // 24,000 + 5% GST

        var edited = await manager.PutAsJsonAsync($"/api/purchase-orders/{order.Id}", new PurchaseOrderSaveRequest
        {
            SupplierId = supplier.Id,
            RowVersion = order.RowVersion,
            Lines = [new PurchaseOrderLineInput { ProductId = product.Id, Quantity = 200m, UnitPrice = 252m }],
        }, TestClients.Json, Ct);
        order = await edited.ReadAsync<PurchaseOrderDto>();
        Assert.Equal(200m, order.Lines[0].Quantity);

        order = await TestData.PlaceAsync(manager, order);
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
        Assert.NotNull(order.OrderedAtUtc);

        var locked = await manager.PutAsJsonAsync($"/api/purchase-orders/{order.Id}", new PurchaseOrderSaveRequest
        {
            SupplierId = supplier.Id,
            RowVersion = order.RowVersion,
            Lines = [new PurchaseOrderLineInput { ProductId = product.Id, Quantity = 1m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        // The next order to the same supplier starts at the price just agreed.
        var next = await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 10m, 0m));
        Assert.Equal(0m, next.Lines[0].UnitPrice); // an explicit price wins
        var defaulted = await manager.PostAsJsonAsync("/api/purchase-orders", new PurchaseOrderSaveRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseOrderLineInput { ProductId = product.Id, Quantity = 10m }],
        }, TestClients.Json, Ct);
        Assert.Equal(252m, (await defaulted.ReadAsync<PurchaseOrderDto>()).Lines[0].UnitPrice);

        // An order with a zero price can't be placed.
        var zero = await manager.PostAsJsonAsync($"/api/purchase-orders/{next.Id}/place", new PurchaseOrderActionRequest { RowVersion = next.RowVersion }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task Orders_above_the_limit_wait_for_someone_who_can_approve_them()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);

        // ₹5,25,000 with GST, above the default ₹5,00,000 limit.
        var order = await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 2000m, 250m));
        Assert.True(order.NeedsApproval);
        order = await TestData.PlaceAsync(manager, order);
        Assert.Equal(PurchaseOrderStatus.PendingApproval, order.Status);

        // The manager can't approve their own order, and nothing can be received yet.
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approve",
            new PurchaseOrderActionRequest { RowVersion = order.RowVersion }, TestClients.Json, Ct)).StatusCode);

        // Sent back with a reason, it's a draft again.
        var rejected = await (await admin.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/reject",
            new PurchaseOrderReasonRequest { RowVersion = order.RowVersion, Reason = "Split it into two deliveries" }, TestClients.Json, Ct)).ReadAsync<PurchaseOrderDto>();
        Assert.Equal(PurchaseOrderStatus.Draft, rejected.Status);
        Assert.Equal("Split it into two deliveries", rejected.RejectionReason);

        order = await TestData.PlaceAsync(manager, rejected);
        var approved = await (await admin.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/approve",
            new PurchaseOrderActionRequest { RowVersion = order.RowVersion }, TestClients.Json, Ct)).ReadAsync<PurchaseOrderDto>();
        Assert.Equal(PurchaseOrderStatus.Ordered, approved.Status);
        Assert.Equal("Test Administrator", approved.ApprovedByName);

        // Someone who may approve places a big order straight away; that counts as the approval.
        var own = await TestData.PlaceAsync(admin, await TestData.CreatePurchaseOrderAsync(admin, supplier.Id, (product.Id, 2000m, 250m)));
        Assert.Equal(PurchaseOrderStatus.Ordered, own.Status);
        Assert.NotNull(own.ApprovedByName);
    }

    [Fact]
    public async Task Receiving_part_of_an_order_then_the_rest_tracks_progress_and_allows_a_small_overage()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 100m, 250m)));

        var first = await TestData.ReceiveAsync(manager, order, 60m);
        Assert.Equal(order.Number, first.PurchaseOrderNumber);
        order = await GetOrderAsync(manager, order.Id);
        Assert.Equal(PurchaseOrderStatus.PartlyReceived, order.Status);
        Assert.Equal(40m, order.Lines[0].QuantityPending);
        Assert.Equal(45m, order.Lines[0].MaxReceivable); // 5% tolerance on 100
        Assert.Single(order.Receipts);

        // More than the tolerance allows is refused, with how much can still come.
        var tooMuch = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 46m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Contains("Only 45", Assert.Single((await tooMuch.ReadAsync<ApiError>()).Errors!["lines[0].quantity"]), StringComparison.Ordinal);

        await TestData.ReceiveAsync(manager, order, 44m);
        order = await GetOrderAsync(manager, order.Id);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(104m, await OnHandAsync(product.Id));

        // A received order can't take more, or be cancelled.
        var cancel = await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/cancel",
            new PurchaseOrderReasonRequest { RowVersion = order.RowVersion, Reason = "Test" }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    [Fact]
    public async Task Draft_orders_cant_be_received_and_a_part_received_order_can_be_closed_short()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var draft = await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 100m, 250m));

        var early = await manager.PostAsJsonAsync($"/api/purchase-orders/{draft.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = draft.RowVersion,
            Lines = [new GoodsReceiptLineInput { PurchaseOrderLineId = draft.Lines[0].Id, Quantity = 10m }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        var order = await TestData.PlaceAsync(manager, draft);
        await TestData.ReceiveAsync(manager, order, 30m);
        order = await GetOrderAsync(manager, order.Id);
        var closed = await (await manager.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/close",
            new PurchaseOrderReasonRequest { RowVersion = order.RowVersion, Reason = "Mill can't supply the balance" }, TestClients.Json, Ct)).ReadAsync<PurchaseOrderDto>();
        Assert.Equal(PurchaseOrderStatus.Closed, closed.Status);
        Assert.Equal("Mill can't supply the balance", closed.CloseReason);
    }

    [Fact]
    public async Task Receiving_re_averages_the_products_cost()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager, costPrice: 200m);
        await TestData.AddOpeningStockAsync(manager, product.Id, 100m);

        // 100 kg at ₹200 plus 50 kg at ₹230.
        await TestData.BuyAsync(manager, (product.Id, 50m, 230m));

        Assert.Equal(210m, await CostAsync(product.Id));
    }

    // ---- Bills and payments ---------------------------------------------------------------------

    [Fact]
    public async Task A_bill_that_matches_can_be_paid_and_counts_as_billed_on_the_order()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 100m, 250m));
        order = await GetOrderAsync(manager, order.Id);

        var bill = await EnterBillAsync(manager, Bill(order) with { SupplierTotal = 26_250m });
        Assert.Matches(@"^PB/\d{2}-\d{2}/\d{5}$", bill.Number);
        Assert.Equal(BillMatchStatus.Matched, bill.MatchStatus);
        Assert.Equal(25_000m, bill.TaxableValue);
        Assert.Equal(625m, bill.CgstAmount);
        Assert.Equal(625m, bill.SgstAmount);
        Assert.Equal(26_250m, bill.GrandTotal);
        Assert.Empty(bill.MatchIssues);
        Assert.Equal(100m, (await GetOrderAsync(manager, order.Id)).Lines[0].QuantityBilled);

        var payment = await manager.PostAsJsonAsync("/api/supplier-payments", new SupplierPaymentCreateRequest
        {
            SupplierBillId = bill.Id, Amount = 26_250m, Mode = PaymentMode.Neft, Reference = "UTIB000123",
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
        bill = await (await manager.GetAsync($"/api/supplier-bills/{bill.Id}", Ct)).ReadAsync<SupplierBillDto>();
        Assert.Equal(SupplierBillStatus.Paid, bill.Status);
        Assert.Single(bill.Payments);

        // Entered twice by mistake: refused.
        var again = await manager.PostAsJsonAsync("/api/supplier-bills", Bill(order, invoice: bill.SupplierInvoiceNumber), TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains(bill.Number, Assert.Single((await again.ReadAsync<ApiError>()).Errors!["supplierInvoiceNumber"]), StringComparison.Ordinal);

        // Paid bills can't be cancelled.
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"/api/supplier-bills/{bill.Id}/cancel",
            new SupplierBillCancelRequest { Reason = "Test" }, TestClients.Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_bill_that_differs_from_its_order_waits_for_approval_before_it_can_be_paid()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(manager);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 100m, 250m));
        order = await GetOrderAsync(manager, order.Id);

        // 5% dearer than the order, and for more than arrived.
        var bill = await EnterBillAsync(manager, Bill(order, price: 262.5m, quantity: 110m) with { SupplierTotal = 30_000m });
        Assert.Equal(BillMatchStatus.NeedsApproval, bill.MatchStatus);
        var issue = Assert.Single(bill.Lines).Issue!;
        Assert.Contains("5% above", issue, StringComparison.Ordinal);
        Assert.Contains("only 100 has been received", issue, StringComparison.Ordinal);
        Assert.Contains(bill.MatchIssues, i => i.Contains("supplier's invoice says ₹30,000.00", StringComparison.Ordinal));

        var pay = new SupplierPaymentCreateRequest { SupplierBillId = bill.Id, Amount = 1000m, Mode = PaymentMode.Neft };
        var blocked = await manager.PostAsJsonAsync("/api/supplier-payments", pay, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var approve = new SupplierBillApproveRequest { RowVersion = bill.RowVersion, Note = "Rate increase agreed by phone." };
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/supplier-bills/{bill.Id}/approve", approve, TestClients.Json, Ct)).StatusCode);
        var approved = await (await admin.PostAsJsonAsync($"/api/supplier-bills/{bill.Id}/approve", approve, TestClients.Json, Ct)).ReadAsync<SupplierBillDto>();
        Assert.Equal(BillMatchStatus.Approved, approved.MatchStatus);
        Assert.Equal("Rate increase agreed by phone.", approved.ApprovalNote);

        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/supplier-payments", pay, TestClients.Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Freight_and_a_dearer_bill_go_into_the_cost_and_cancelling_the_bill_takes_them_out()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager, costPrice: null);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 100m, 200m));
        order = await GetOrderAsync(manager, order.Id);
        Assert.Equal(200m, await CostAsync(product.Id));

        // ₹1,000 freight on 100 kg still on hand adds ₹10 a kg; ₹1 a kg dearer adds another ₹1.
        var bill = await EnterBillAsync(manager, Bill(order, price: 201m) with { ChargesAmount = 1000m, ChargesGstRate = 18m, ChargesDescription = "Lorry freight" });
        Assert.Equal(211m, await CostAsync(product.Id));
        Assert.Equal(1000m, bill.ChargesAmount);
        Assert.Equal(20_100m, bill.TaxableValue);
        Assert.Equal(BillMatchStatus.Matched, bill.MatchStatus); // 0.5% is within the tolerance

        var cancelled = await (await manager.PostAsJsonAsync($"/api/supplier-bills/{bill.Id}/cancel",
            new SupplierBillCancelRequest { Reason = "Entered against the wrong order" }, TestClients.Json, Ct)).ReadAsync<SupplierBillDto>();
        Assert.Equal(SupplierBillStatus.Cancelled, cancelled.Status);
        Assert.Equal(200m, await CostAsync(product.Id));
        Assert.Equal(0m, (await GetOrderAsync(manager, order.Id)).Lines[0].QuantityBilled);

        // The same supplier invoice can now be entered again, correctly.
        await EnterBillAsync(manager, Bill(order, invoice: bill.SupplierInvoiceNumber));
    }

    [Fact]
    public async Task A_supplier_in_another_state_bills_IGST()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager, stateCode: "29", gstin: TestData.KarnatakaGstin);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (product.Id, 100m, 250m)));
        Assert.True(order.IsInterState);
        await TestData.ReceiveAsync(manager, order, 100m);
        order = await GetOrderAsync(manager, order.Id);

        var bill = await EnterBillAsync(manager, Bill(order));

        Assert.True(bill.IsInterState);
        Assert.Equal(1250m, bill.IgstAmount);
        Assert.Equal(0m, bill.CgstAmount + bill.SgstAmount);
    }

    [Fact]
    public async Task Cash_paid_to_one_supplier_in_a_day_is_capped_at_ten_thousand_rupees()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 100m, 250m));
        var bill = await EnterBillAsync(manager, Bill(await GetOrderAsync(manager, order.Id)));

        var cash = new SupplierPaymentCreateRequest { SupplierBillId = bill.Id, Amount = 10_000m, Mode = PaymentMode.Cash };
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/supplier-payments", cash, TestClients.Json, Ct)).StatusCode);
        var more = await manager.PostAsJsonAsync("/api/supplier-payments", cash with { Amount = 1m }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, more.StatusCode);
        Assert.Contains("40A(3)", Assert.Single((await more.ReadAsync<ApiError>()).Errors!["amount"]), StringComparison.Ordinal);
    }

    // ---- Reordering -----------------------------------------------------------------------------

    [Fact]
    public async Task Low_stock_turns_into_draft_orders_one_per_preferred_supplier()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var supplierA = await TestData.CreateSupplierAsync(manager);
        var supplierB = await TestData.CreateSupplierAsync(manager);
        var yarn = await SetPreferredSupplierAsync(manager, await TestData.CreateProductAsync(manager, reorderLevel: 100m), supplierA.Id);
        var fabric = await SetPreferredSupplierAsync(manager, await TestData.CreateProductAsync(manager, reorderLevel: 100m), supplierA.Id);
        var cones = await SetPreferredSupplierAsync(manager, await TestData.CreateProductAsync(manager, reorderLevel: 100m), supplierB.Id);
        var orphan = await TestData.CreateProductAsync(manager, reorderLevel: 100m);

        var candidates = await (await manager.GetAsync("/api/purchase-orders/reorder-candidates", Ct)).ReadAsync<List<ReorderCandidateDto>>();
        var yarnCandidate = Assert.Single(candidates, c => c.ProductId == yarn.Id);
        Assert.Equal(supplierA.Id, yarnCandidate.SupplierId);
        Assert.Equal(500m, yarnCandidate.SuggestedQuantity); // the product's reorder quantity
        Assert.Contains(candidates, c => c.ProductId == orphan.Id && c.SupplierId == null);

        var drafted = await manager.PostAsJsonAsync("/api/purchase-orders/reorder-drafts", new ReorderDraftRequest
        {
            Lines =
            [
                new ReorderDraftLineInput { ProductId = yarn.Id, Quantity = 500m },
                new ReorderDraftLineInput { ProductId = fabric.Id, Quantity = 300m },
                new ReorderDraftLineInput { ProductId = cones.Id, Quantity = 50m },
            ],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, drafted.StatusCode);
        var orders = await drafted.ReadAsync<List<PurchaseOrderListItemDto>>();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(PurchaseOrderStatus.Draft, o.Status));
        Assert.Equal(2, Assert.Single(orders, o => o.SupplierId == supplierA.Id).LineCount);

        // Now on order, they're no longer suggested.
        candidates = await (await manager.GetAsync("/api/purchase-orders/reorder-candidates", Ct)).ReadAsync<List<ReorderCandidateDto>>();
        Assert.DoesNotContain(candidates, c => c.ProductId == yarn.Id);

        var noSupplier = await manager.PostAsJsonAsync("/api/purchase-orders/reorder-drafts",
            new ReorderDraftRequest { Lines = [new ReorderDraftLineInput { ProductId = orphan.Id, Quantity = 10m }] }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noSupplier.StatusCode);
    }

    private static async Task<ProductDto> SetPreferredSupplierAsync(HttpClient client, ProductDto product, int supplierId)
    {
        var response = await client.PutAsJsonAsync($"/api/products/{product.Id}", new ProductSaveRequest
        {
            Sku = product.Sku, Name = product.Name, CategoryId = product.CategoryId, HsnCodeId = product.HsnCodeId, Unit = product.Unit,
            ListPrice = product.ListPrice, CostPrice = product.CostPrice, ReorderLevel = product.ReorderLevel, ReorderQuantity = product.ReorderQuantity,
            LeadTimeDays = product.LeadTimeDays, PreferredSupplierId = supplierId, RowVersion = product.RowVersion,
        }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        var updated = await response.ReadAsync<ProductDto>();
        Assert.Equal(supplierId, updated.PreferredSupplierId);
        return updated;
    }

    // ---- Reports and the audit trail ------------------------------------------------------------

    [Fact]
    public async Task Payables_ageing_agrees_with_the_bills_summary_and_the_purchase_register_lists_input_GST()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateProductAsync(manager);
        var (order, _) = await TestData.BuyAsync(manager, (product.Id, 10m, 250m));
        var bill = await EnterBillAsync(manager, Bill(await GetOrderAsync(manager, order.Id)));

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

    [Fact]
    public async Task Purchasing_is_recorded_in_the_audit_trail()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var product = await TestData.CreateProductAsync(manager);
        var (order, receipt) = await TestData.BuyAsync(manager, (product.Id, 10m, 250m));

        var entries = await (await admin.GetAsync($"/api/audit?entityType=Purchase%20order&entityId={order.Id}", Ct)).ReadAsync<PagedResult<SalesApp.Core.Contracts.Audit.AuditEntryDto>>();
        Assert.Contains(entries.Items, e => e.Action == AuditAction.Created);
        Assert.Contains(entries.Items, e => e.Changes.Any(c => c.Field == "Status" && c.New == "Ordered"));
        Assert.Contains(entries.Items, e => e.Changes.Any(c => c.Field == "Status" && c.New == "Received"));

        var grn = await (await admin.GetAsync($"/api/audit?entityType=Goods%20receipt&entityId={receipt.Id}", Ct)).ReadAsync<PagedResult<SalesApp.Core.Contracts.Audit.AuditEntryDto>>();
        Assert.Contains(grn.Items, e => e.Changes.Any(c => c.Field == "Purchase order" && c.New == order.Number));
    }
}
