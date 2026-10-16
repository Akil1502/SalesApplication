using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI.Claude;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class BillReadingTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% a supplier's invoice\n");

    private (WebApplicationFactory<Program> App, FakeClaudeGateway Claude) WithFakeClaude()
    {
        var claude = new FakeClaudeGateway();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IClaudeGateway>(claude)));
        return (app, claude);
    }

    private static MultipartFormDataContent Form(int purchaseOrderId, byte[] file)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent
        {
            { new StringContent(purchaseOrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "purchaseOrderId" },
            { content, "file", "bill.pdf" },
        };
    }

    private static object Line(string sourceText, int poLineId, decimal quantity, decimal unitPrice, decimal gstRate = 5m, string confidence = "high") =>
        new { sourceText, poLineId, quantity, unitPrice, gstRate, confidence, note = "" };

    private static object Reply(object[] lines, string gstin, string invoiceNumber, decimal grandTotal, object[]? charges = null, string poNumber = "", bool isInvoice = true) => new
    {
        isInvoice,
        supplierName = "Lakshmi Spinning Mills",
        supplierGstin = gstin,
        invoiceNumber,
        invoiceDate = "2026-10-10",
        dueDate = "",
        poNumber,
        lines,
        charges = charges ?? [],
        taxableTotal = 0,
        taxTotal = 0,
        grandTotal,
        unmatched = Array.Empty<string>(),
        notes = "",
    };

    [Fact]
    public async Task A_bill_is_read_against_its_order_checked_and_nothing_is_saved()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager);
        var fabric = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (yarn.Id, 100m, 250m), (fabric.Id, 50m, 90m)));
        await TestData.ReceiveAsync(manager, order, 100m, 50m);

        // Lines: yarn matched, fabric matched to an id that isn't on this order. The GSTIN is someone else's and
        // the printed total doesn't add up.
        claude.Reply = _ => Reply(
            [Line("Cotton yarn 30s, 100 kg", order.Lines[0].Id, 100m, 250m), Line("Poplin 50 mtr", 999_999, 50m, 90m)],
            gstin: TestData.KarnatakaGstin, invoiceNumber: "LSM/26-27/0412", grandTotal: 99_999m,
            charges: [new { description = "Freight", amount = 1200m, gstRate = 18m }], poNumber: "PO/26-27/99999");

        var response = await manager.PostAsync("/api/supplier-bills/read", Form(order.Id, Pdf), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await response.ReadAsync<BillExtractionDto>();
        Assert.Equal(BillSource.AiPdf, read.Source);
        Assert.Equal("LSM/26-27/0412", read.SupplierInvoiceNumber);
        Assert.Equal(new DateOnly(2026, 10, 10), read.BillDate);
        Assert.Equal(order.Lines[0].Id, read.Lines[0].PurchaseOrderLineId);
        Assert.Equal(250m, read.Lines[0].OrderedPrice);
        Assert.Null(read.Lines[1].PurchaseOrderLineId);
        Assert.Equal(ExtractionConfidence.Low, read.Lines[1].Confidence);
        Assert.Equal(1200m, read.ChargesAmount);
        Assert.Equal(18m, read.ChargesGstRate);
        Assert.Contains(read.Warnings, w => w.Contains("couldn't be matched", StringComparison.Ordinal));
        Assert.Contains(read.Warnings, w => w.Contains("isn't " + supplier.Name, StringComparison.Ordinal));
        Assert.Contains(read.Warnings, w => w.Contains("quotes order PO/26-27/99999", StringComparison.Ordinal));
        Assert.Contains(read.Warnings, w => w.Contains("bill's total is ₹99,999.00", StringComparison.Ordinal));

        // Claude saw the order's lines and number, after the cached instructions.
        var request = Assert.Single(claude.Requests);
        Assert.Equal("BillReading", request.Feature);
        Assert.True(request.System[0].CacheAfter);
        var userText = request.Content.OfType<ClaudeText>().Single().Text;
        Assert.Contains(order.Number, userText, StringComparison.Ordinal);
        Assert.Contains($"{order.Lines[1].Id} | {fabric.Sku}", userText, StringComparison.Ordinal);
        Assert.IsType<ClaudePdf>(request.Content[0]);

        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().SupplierBills.AnyAsync(b => b.PurchaseOrderId == order.Id, Ct));
    }

    [Fact]
    public async Task An_invoice_already_entered_is_pointed_out()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (yarn.Id, 100m, 250m)));
        await TestData.ReceiveAsync(manager, order, 100m);
        var entered = await manager.PostAsync("/api/supplier-bills", System.Net.Http.Json.JsonContent.Create(new SupplierBillCreateRequest
        {
            PurchaseOrderId = order.Id,
            SupplierInvoiceNumber = "LSM-77",
            Lines = [new SupplierBillLineInput { PurchaseOrderLineId = order.Lines[0].Id, Quantity = 100m, UnitPrice = 250m }],
        }, options: TestClients.Json), Ct);
        entered.EnsureSuccessStatusCode();

        // The same invoice, adding up exactly: 25,000 + 5% GST.
        claude.Reply = _ => Reply([Line("Yarn", order.Lines[0].Id, 100m, 250m)], gstin: supplier.Gstin!, invoiceNumber: "LSM-77", grandTotal: 26_250m);
        var read = await (await manager.PostAsync("/api/supplier-bills/read", Form(order.Id, Pdf), Ct)).ReadAsync<BillExtractionDto>();

        var warning = Assert.Single(read.Warnings);
        Assert.Contains("already entered", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_pdfs_and_images_are_read_and_only_with_the_right()
    {
        var (app, _) = WithFakeClaude();
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager);
        var supplier = await TestData.CreateSupplierAsync(manager);
        var order = await TestData.PlaceAsync(manager, await TestData.CreatePurchaseOrderAsync(manager, supplier.Id, (yarn.Id, 100m, 250m)));

        var text = await manager.PostAsync("/api/supplier-bills/read", Form(order.Id, Encoding.ASCII.GetBytes("not a pdf")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
        Assert.Contains("file", (await text.ReadAsync<ApiError>()).Errors!.Keys);

        var (clerk, _) = await app.CreateClientWithPermissionsAsync(Permissions.SupplierBillsEdit);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync("/api/supplier-bills/read", Form(order.Id, Pdf), Ct)).StatusCode);
    }
}
