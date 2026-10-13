using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI.Claude;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

/// <summary>Stands in for Claude: records what it was sent and answers with whatever JSON the test sets.</summary>
internal sealed class FakeClaudeGateway : IClaudeGateway
{
    public bool IsAvailable => true;
    public List<ClaudeRequest> Requests { get; } = [];
    public Func<ClaudeRequest, object> Reply { get; set; } = _ => new { };

    public Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var reply = Reply(request);
        var text = reply as string ?? JsonSerializer.Serialize(reply, JsonSerializerOptions.Web);
        return Task.FromResult(new ClaudeResponse(text, new ClaudeUsage("claude-opus-5-5", 150, 400, 2100, 0, 0.0092m, 1800), "msg_test"));
    }
}

public sealed class SmartOrderEntryTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (WebApplicationFactory<Program> App, FakeClaudeGateway Claude) WithFakeClaude(params (string Key, string Value)[] settings)
    {
        var claude = new FakeClaudeGateway();
        var app = factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<IClaudeGateway>(claude));
        });
        return (app, claude);
    }

    private static MultipartFormDataContent Form(string? text = null, byte[]? file = null, string fileName = "order.bin")
    {
        var form = new MultipartFormDataContent();
        if (text is not null)
        {
            form.Add(new StringContent(text), "text");
        }

        if (file is not null)
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "file", fileName);
        }

        return form;
    }

    private static object Line(string sourceText, int productId, decimal quantity = 0, decimal unitPrice = 0, string confidence = "high", string sourceUnit = "", string note = "") =>
        new { sourceText, productId, quantity, sourceUnit, unitPrice, confidence, note };

    private static object Reply(object[] lines, string customerName = "", bool isOrder = true, string reference = "", string deliveryDate = "", string notes = "", string[]? unmatched = null) => new
    {
        isOrder,
        customer = new { name = customerName, gstin = "", phone = "", city = "", contactPerson = "" },
        customerReference = reference,
        requestedDeliveryDate = deliveryDate,
        notes,
        lines,
        unmatched = unmatched ?? [],
    };

    private async Task<int> OrderCountAsync(int customerId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SalesOrders.CountAsync(o => o.CustomerId == customerId, Ct);
    }

    [Fact]
    public async Task Without_an_api_key_ai_is_reported_off_and_extraction_is_refused()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var status = await (await rep.GetAsync("/api/ai/status", Ct)).ReadAsync<AiStatusDto>();
        Assert.False(status.Enabled);
        Assert.False(status.OrderEntry);
        Assert.Contains("API key", status.Reason, StringComparison.Ordinal);

        var response = await rep.PostAsync("/api/orders/smart-entry/extract", Form("30s combed 500 kg"), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("API key", (await response.ReadAsync<ApiError>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_whatsapp_message_becomes_a_checked_draft_and_nothing_is_saved()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager, listPrice: 300m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);

        claude.Reply = _ => Reply(
            [
                Line("30s cc 500kg @310", yarn.Id, quantity: 500, unitPrice: 310),
                Line("special blend 10 nos", productId: 987654, quantity: 10),
                Line("red thread", productId: 0, confidence: "low"),
            ],
            customerName: customer.Name, reference: "PO-77", deliveryDate: "2026-10-20", notes: "Send by KPN transport", unmatched: ["2 box ??"]);

        var response = await rep.PostAsync("/api/orders/smart-entry/extract", Form("Hi sir, 30s cc 500kg @310, special blend 10 nos, red thread. PO-77"), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.ReadAsync<OrderExtractionDto>();

        Assert.Equal(OrderSource.AiText, draft.Source);
        Assert.True(draft.LooksLikeOrder);
        Assert.Equal(customer.Id, draft.Customer.CustomerId);
        Assert.Equal(CustomerMatchSource.Name, draft.Customer.MatchedBy);
        Assert.Equal("PO-77", draft.CustomerReference);
        Assert.Equal(new DateOnly(2026, 10, 20), draft.RequestedDeliveryDate);
        Assert.Equal("Send by KPN transport", draft.Notes);
        Assert.Equal(["2 box ??"], draft.Unmatched);

        var matched = draft.Lines[0];
        Assert.Equal(yarn.Id, matched.Product?.Id);
        Assert.Equal(yarn.Sku, matched.Product?.Sku);
        Assert.Equal(500m, matched.Quantity);
        Assert.Equal(300m, matched.Product?.ListPrice); // from the price list, not the message
        Assert.Equal(310m, matched.StatedPrice);
        Assert.Equal(0.0333m, matched.PriceVariance);
        Assert.Equal(ProductMatchSource.Ai, matched.MatchedBy);
        Assert.Equal(ExtractionConfidence.High, matched.Confidence);

        // A product id the model invented is dropped, whatever confidence it claimed.
        var invented = draft.Lines[1];
        Assert.Null(invented.Product);
        Assert.Equal(ExtractionConfidence.Low, invented.Confidence);
        Assert.Contains("isn't in the catalog", invented.Note, StringComparison.Ordinal);

        Assert.Null(draft.Lines[2].Product);
        Assert.Null(draft.Lines[2].Quantity);
        Assert.Equal(2100, draft.Usage.CacheReadTokens);

        // What Claude was sent: cached instructions + catalog first, the dated message after.
        var request = Assert.Single(claude.Requests);
        Assert.Equal("OrderEntry", request.Feature);
        Assert.Equal([false, true], request.System.Select(b => b.CacheAfter));
        Assert.Contains($"{yarn.Id} | {yarn.Sku} | Test yarn | Kg", request.System[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("250", request.System[1].Text, StringComparison.Ordinal); // cost prices never leave the server
        var text = Assert.IsType<ClaudeText>(Assert.Single(request.Content));
        Assert.Contains("Today's date (India):", text.Text, StringComparison.Ordinal);
        Assert.Contains("<customer_message>\nHi sir, 30s cc 500kg", text.Text, StringComparison.Ordinal);
        Assert.NotNull(request.OutputSchema);

        Assert.Equal(0, await OrderCountAsync(customer.Id));
    }

    [Fact]
    public async Task Confirming_saves_the_order_and_teaches_the_wording_for_next_time()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager);
        var customer = await TestData.CreateCustomerAsync(manager);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        var wording = $"Kora special {Guid.NewGuid():N}"[..24];

        var created = await rep.PostAsJsonAsync("/api/orders/smart-entry", new SmartOrderConfirmRequest
        {
            Order = new OrderSaveRequest
            {
                CustomerId = customer.Id,
                CustomerReference = "PO-1",
                Lines = [new OrderLineInput { ProductId = yarn.Id, Quantity = 120m }],
            },
            Source = OrderSource.AiText,
            Corrections = [new AliasCorrection { SourceText = wording, ProductId = yarn.Id }],
        }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.ReadAsync<OrderDto>();
        Assert.Equal(OrderSource.AiText, order.Source);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
        Assert.Equal(1, await OrderCountAsync(customer.Id));

        // Next time the same wording (any case or punctuation) maps to the product even if Claude doesn't match it.
        claude.Reply = _ => Reply([Line($"{wording.ToUpperInvariant()}!!", productId: 0, quantity: 40, confidence: "low")]);
        var draft = await (await rep.PostAsync("/api/orders/smart-entry/extract", Form($"{wording} 40 kg"), Ct)).ReadAsync<OrderExtractionDto>();

        var line = Assert.Single(draft.Lines);
        Assert.Equal(yarn.Id, line.Product?.Id);
        Assert.Equal(ProductMatchSource.Alias, line.MatchedBy);
        Assert.Equal(ExtractionConfidence.Medium, line.Confidence);
        Assert.Contains(wording, claude.Requests[^1].System[1].Text, StringComparison.Ordinal); // and Claude now sees it in the catalog
    }

    [Fact]
    public async Task Confirm_only_accepts_ai_sources()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var yarn = await TestData.CreateProductAsync(manager);
        var customer = await TestData.CreateCustomerAsync(manager);

        var response = await manager.PostAsJsonAsync("/api/orders/smart-entry", new SmartOrderConfirmRequest
        {
            Order = new OrderSaveRequest { CustomerId = customer.Id, Lines = [new OrderLineInput { ProductId = yarn.Id, Quantity = 1m }] },
            Source = OrderSource.Manual,
        }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await OrderCountAsync(customer.Id));
    }

    [Fact]
    public async Task Photos_go_to_claude_as_images_and_other_files_are_rejected()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Reply = _ => Reply([]);

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var draft = await (await rep.PostAsync("/api/orders/smart-entry/extract", Form(file: png, fileName: "po.jpg"), Ct)).ReadAsync<OrderExtractionDto>();
        Assert.Equal(OrderSource.AiImage, draft.Source);
        var request = Assert.Single(claude.Requests);
        var image = Assert.IsType<ClaudeImage>(request.Content[0]);
        Assert.Equal("image/png", image.MediaType); // from the bytes, not the .jpg name
        Assert.Contains("The attached file is the customer's order.", Assert.IsType<ClaudeText>(request.Content[1]).Text, StringComparison.Ordinal);

        var word = await rep.PostAsync("/api/orders/smart-entry/extract", Form(file: Encoding.ASCII.GetBytes("PK\u0003\u0004 not a pdf"), fileName: "po.pdf"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, word.StatusCode);
        Assert.True((await word.ReadAsync<ApiError>()).Errors!.ContainsKey("file"));
        Assert.Single(claude.Requests); // never sent

        var empty = await rep.PostAsync("/api/orders/smart-entry/extract", Form(text: "   "), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Content_that_isnt_an_order_gives_no_lines()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Reply = _ => Reply([Line("payment done", productId: 0)], isOrder: false);

        var draft = await (await rep.PostAsync("/api/orders/smart-entry/extract", Form("Sir payment done for last bill"), Ct)).ReadAsync<OrderExtractionDto>();

        Assert.False(draft.LooksLikeOrder);
        Assert.Empty(draft.Lines);
    }

    [Fact]
    public async Task An_unreadable_reply_is_a_clean_error()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Reply = _ => "{ not json";

        var response = await rep.PostAsync("/api/orders/smart-entry/extract", Form("30s 100 kg"), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("couldn't be read", (await response.ReadAsync<ApiError>()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_beyond_the_per_minute_limit_are_turned_away()
    {
        var (app, claude) = WithFakeClaude(("Ai:RequestsPerMinutePerUser", "2"));
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Reply = _ => Reply([]);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await rep.PostAsync("/api/orders/smart-entry/extract", Form("30s 100 kg"), Ct)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(2, claude.Requests.Count);
    }

    [Fact]
    public async Task A_user_over_their_daily_budget_is_stopped_before_any_call()
    {
        // A key is configured (so AI counts as available) but it's fake: the budget check must stop the request first.
        var app = factory.WithWebHostBuilder(builder => builder.UseSetting("Anthropic:ApiKey", "test-key-that-is-never-sent"));
        var (rep, userId) = await app.CreateClientAsync(Roles.SalesRep);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiUsageLogs.Add(new AiUsageLog
            {
                Feature = "OrderEntry", Model = "claude-opus-5-5", UserId = userId, CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
                CostUsd = 2.5m, Outcome = AiOutcome.Success,
            });
            await db.SaveChangesAsync(Ct);
        }

        Assert.True((await (await rep.GetAsync("/api/ai/status", Ct)).ReadAsync<AiStatusDto>()).Enabled);
        var response = await rep.PostAsync("/api/orders/smart-entry/extract", Form("30s 100 kg"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("usage limit", (await response.ReadAsync<ApiError>()).Message, StringComparison.Ordinal);
    }
}
