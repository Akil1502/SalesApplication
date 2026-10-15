using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI;
using SalesApp.AI.Assistant;
using SalesApp.AI.Claude;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class AssistantTests(SalesAppFactory factory)
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

    private static ClaudeToolUseBlock Tool(string id, string name, object input) =>
        new(id, name, JsonSerializer.SerializeToElement(input).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));

    private sealed record Sale(CustomerDto Customer, InvoiceDto Invoice);

    /// <summary>A customer with contact details and one unpaid invoice.</summary>
    private static async Task<Sale> SaleAsync(HttpClient manager)
    {
        var response = await manager.PostAsJsonAsync("/api/customers", new CustomerSaveRequest
        {
            Name = $"Kavya Traders {Guid.NewGuid():N}"[..24],
            ContactPerson = "Kavya Raman",
            Email = "accounts@kavya.example",
            Phone = "+919876543210",
            BillingAddress = new AddressInput { Line1 = "4 Mill Road", City = "Erode", StateCode = "33", Pincode = "638001" },
            PaymentTermsDays = 30,
        }, TestClients.Json, Ct);
        response.EnsureSuccessStatusCode();
        var customer = await response.ReadAsync<CustomerDto>();
        var product = await TestData.CreateStockedProductAsync(manager, 50m, listPrice: 500m);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (product.Id, 4m)));
        return new Sale(customer, await TestData.InvoiceAsync(manager, order.Id));
    }

    /// <summary>Sends a message and reads the whole event stream into (event, data) pairs.</summary>
    private static async Task<List<(string Event, JsonElement Data)>> SendAsync(HttpClient client, string message, Guid? conversationId = null)
    {
        var response = await client.PostAsJsonAsync("/api/assistant/messages", new AssistantSendRequest { ConversationId = conversationId, Message = message }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static List<(string Event, JsonElement Data)> Parse(string stream)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var frame in stream.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = frame.Split('\n');
            var name = lines.First(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
            var data = lines.First(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
            events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }

        return events;
    }

    private static Guid ConversationId(List<(string Event, JsonElement Data)> events) =>
        events.Single(e => e.Event == "conversation").Data.GetProperty("conversationId").GetGuid();

    private static string Text(List<(string Event, JsonElement Data)> events) =>
        string.Concat(events.Where(e => e.Event == "text").Select(e => e.Data.GetProperty("delta").GetString()));

    private async Task<List<AiMessage>> MessagesAsync(Guid conversationId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiMessages
            .Where(m => m.ConversationId == conversationId).OrderBy(m => m.Sequence).ToListAsync(Ct);
    }

    [Fact]
    public async Task A_reply_streams_with_its_lookups_and_the_conversation_is_saved()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var sale = await SaleAsync(manager);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);

        claude.Turns.Enqueue(_ => [new ClaudeThinkingBlock("", "sig-1"), Tool("tu_1", AssistantPrompt.SearchCustomers, new { query = "Kavya Traders" })]);
        claude.Turns.Enqueue(_ => [Tool("tu_2", AssistantPrompt.ListInvoices, new { customerId = sale.Customer.Id, status = "outstanding" })]);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock($"{sale.Customer.Name} owes **₹{sale.Invoice.GrandTotal.ToString("N2", CultureInfo.InvariantCulture)}** on {sale.Invoice.Number}.")]);

        var events = await SendAsync(rep, "What does Kavya Traders owe us?");

        Assert.Equal("conversation", events[0].Event);
        var id = ConversationId(events);
        var tools = events.Where(e => e.Event == "tool").Select(e => e.Data.Deserialize<AssistantToolDto>(TestClients.Json)!).ToList();
        Assert.Equal(["tu_1", "tu_1", "tu_2", "tu_2"], tools.Select(t => t.Id)); // running, then done
        Assert.Equal([null, true, null, true], tools.Select(t => t.Succeeded));
        Assert.Equal($"Listed outstanding invoices for {sale.Customer.Name}", tools[3].Label);
        Assert.Contains(sale.Invoice.Number, Text(events), StringComparison.Ordinal);
        Assert.Equal("done", events[^1].Event);

        // Claude got the customer's id and the invoice from the tools; phone and email are never sent to it.
        var searchResult = Assert.IsType<ClaudeToolResultBlock>(Assert.Single(claude.Conversations[1].Messages[^1].Blocks));
        Assert.Contains($"\"id\":{sale.Customer.Id}", searchResult.Content, StringComparison.Ordinal);
        var invoices = Assert.IsType<ClaudeToolResultBlock>(Assert.Single(claude.Conversations[2].Messages[^1].Blocks));
        Assert.Contains(sale.Invoice.Number, invoices.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("9876543210", searchResult.Content + invoices.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("kavya.example", searchResult.Content + invoices.Content, StringComparison.Ordinal);

        // The first user message carries today's date, and the system prompt doesn't (it must never change).
        var first = claude.Conversations[0];
        Assert.StartsWith("<context>Today's date", ((ClaudeTextBlock)first.Messages[0].Blocks[0]).Text, StringComparison.Ordinal);
        Assert.Equal("What does Kavya Traders owe us?", ((ClaudeTextBlock)first.Messages[0].Blocks[1]).Text);
        Assert.True(first.System[^1].CacheAfter);
        Assert.Contains($"Test {Roles.SalesRep}", first.System[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("rpt_mgr", first.System[^1].Text, StringComparison.Ordinal);

        // Saved: question, two rounds of (tool call, results), and the answer.
        var messages = await MessagesAsync(id);
        Assert.Equal([AiMessageRole.User, AiMessageRole.Assistant, AiMessageRole.User, AiMessageRole.Assistant, AiMessageRole.User, AiMessageRole.Assistant],
            messages.Select(m => m.Role));

        var view = await (await rep.GetAsync($"/api/assistant/conversations/{id}", Ct)).ReadAsync<AssistantConversationDto>();
        Assert.Equal("What does Kavya Traders owe us?", view.Title);
        Assert.Equal([AssistantItemKind.User, AssistantItemKind.Tool, AssistantItemKind.Tool, AssistantItemKind.Assistant], view.Items.Select(i => i.Kind));
        Assert.Equal("What does Kavya Traders owe us?", view.Items[0].Text);
        Assert.Equal(tools[3], view.Items[2].Tool); // the same label as live
        Assert.Contains(sale.Invoice.Number, view.Items[3].Text, StringComparison.Ordinal);

        var list = await (await rep.GetAsync("/api/assistant/conversations", Ct)).ReadAsync<List<AssistantConversationSummaryDto>>();
        Assert.Equal(id, list[0].Id);
    }

    [Fact]
    public async Task The_next_message_replays_the_saved_history_exactly()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [new ClaudeThinkingBlock("", "sig-abc"), Tool("tu_1", AssistantPrompt.SearchProducts, new { query = "cotton" })]);
        claude.Turns.Enqueue(_ => [new ClaudeThinkingBlock("", "sig-def"), new ClaudeTextBlock("We stock several cotton yarns.")]);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("The 40s count is the most popular.")]);

        var id = ConversationId(await SendAsync(rep, "Which cotton yarns do we have?"));
        var events = await SendAsync(rep, "Which one sells best?", id);

        Assert.DoesNotContain(events, e => e.Event == "conversation");
        var before = claude.Conversations[1];
        var after = claude.Conversations[2];
        Assert.Equal(before.System, after.System);
        Assert.Same(before.Tools, after.Tools);

        // Everything sent before, plus the reply, is sent again byte for byte (thinking signatures included);
        // only the new user message is added.
        var expected = before.Messages.Append(new ClaudeMessage(true, [new ClaudeThinkingBlock("", "sig-def"), new ClaudeTextBlock("We stock several cotton yarns.")]));
        Assert.Equal(expected.Select(m => ClaudeBlockJson.Serialize(m.Blocks)), after.Messages.SkipLast(1).Select(m => ClaudeBlockJson.Serialize(m.Blocks)));
        Assert.Equal("Which one sells best?", ((ClaudeTextBlock)after.Messages[^1].Blocks[1]).Text);
        Assert.Contains(after.Messages[1].Blocks, b => b is ClaudeThinkingBlock { Signature: "sig-abc" });
    }

    [Fact]
    public async Task Stopping_keeps_what_was_shown_and_the_conversation_carries_on()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("Here is a long answer that the user stops")]);
        claude.AfterStreamedText = token => Task.Delay(Timeout.Infinite, token);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/messages")
        {
            Content = JsonContent.Create(new AssistantSendRequest { Message = "Tell me everything" }, options: TestClients.Json),
        };
        var response = await rep.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop.Token));
        var seen = new StringBuilder();
        List<(string Event, JsonElement Data)> received = [];
        while (Text(received) != "Here is a long answer that the user stops")
        {
            seen.Append((char)reader.Read());
            if (seen.ToString().EndsWith("\n\n", StringComparison.Ordinal))
            {
                received = Parse(seen.ToString());
            }
        }

        var id = ConversationId(received);
        claude.AfterStreamedText = null;
        await stop.CancelAsync(); // the Stop button
        response.Dispose();

        AssistantConversationDto view;
        var waited = 0;
        do
        {
            await Task.Delay(100, Ct);
            view = await (await rep.GetAsync($"/api/assistant/conversations/{id}", Ct)).ReadAsync<AssistantConversationDto>();
        }
        while (view.Items.Count < 2 && (waited += 100) < 5000);

        var stopped = view.Items[^1];
        Assert.Equal(AssistantItemKind.Assistant, stopped.Kind);
        Assert.True(stopped.Interrupted);
        Assert.Equal("Here is a long answer that the user stops", stopped.Text);
        Assert.Equal(AiMessageStatus.Stopped, (await MessagesAsync(id))[^1].Status);

        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("A shorter answer.")]);
        var next = await SendAsync(rep, "Just the summary, please", id);
        Assert.Equal("A shorter answer.", Text(next));
        Assert.Equal("Here is a long answer that the user stops", ((ClaudeTextBlock)claude.Conversations[^1].Messages[1].Blocks[0]).Text);
    }

    [Fact]
    public async Task Drafts_are_addressed_from_the_customer_record_and_checked_against_the_data()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var sale = await SaleAsync(manager);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        var outstanding = $"₹{sale.Invoice.GrandTotal.ToString("N2", CultureInfo.InvariantCulture)}";
        var body = $"Dear Kavya Raman,\n\nInvoice {sale.Invoice.Number} for {outstanding} is due. Invoice INV/26-27/99999 for ₹1,234.56 too.\n\nRegards";

        claude.Turns.Enqueue(_ => [Tool("tu_1", AssistantPrompt.ListInvoices, new { customerId = sale.Customer.Id, status = "outstanding" })]);
        claude.Turns.Enqueue(_ => [Tool("tu_2", AssistantPrompt.CreateDraft, new
        {
            kind = "email", purpose = "payment_reminder", customerId = sale.Customer.Id, subject = "Payment reminder", body,
        })]);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("I've drafted the reminder.")]);

        var events = await SendAsync(rep, "Draft a payment reminder for Kavya Traders");

        var draft = events.Single(e => e.Event == "draft").Data.Deserialize<AssistantDraftDto>(TestClients.Json)!;
        Assert.Equal(AiDraftKind.Email, draft.Kind);
        Assert.Equal("accounts@kavya.example", draft.Email); // from the customer record, not from Claude
        Assert.Equal("+919876543210", draft.Phone);
        Assert.Equal("Kavya Raman", draft.ContactPerson);
        Assert.Equal("Payment reminder", draft.Subject);
        Assert.Equal(body, draft.Body);
        Assert.Equal(2, draft.Warnings.Count);
        Assert.Contains(draft.Warnings, w => w.Contains("INV/26-27/99999", StringComparison.Ordinal));
        Assert.Contains(draft.Warnings, w => w.Contains("₹1,234.56", StringComparison.Ordinal));
        Assert.DoesNotContain(draft.Warnings, w => w.Contains(sale.Invoice.Number, StringComparison.Ordinal) || w.Contains(outstanding, StringComparison.Ordinal));

        // Claude is told the draft was shown and nothing was sent, with the warnings so it can say so.
        var result = Assert.IsType<ClaudeToolResultBlock>(Assert.Single(claude.Conversations[2].Messages[^1].Blocks));
        Assert.Contains("Nothing was sent", result.Content, StringComparison.Ordinal);
        Assert.Contains("INV/26-27/99999", result.Content, StringComparison.Ordinal);

        // The saved conversation shows the draft in place.
        var view = await (await rep.GetAsync($"/api/assistant/conversations/{ConversationId(events)}", Ct)).ReadAsync<AssistantConversationDto>();
        Assert.Equal([AssistantItemKind.User, AssistantItemKind.Tool, AssistantItemKind.Draft, AssistantItemKind.Assistant], view.Items.Select(i => i.Kind));
        Assert.Equivalent(draft, view.Items[2].Draft, strict: true);
    }

    [Fact]
    public async Task Another_invoice_number_from_a_different_customer_is_flagged()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var mine = await SaleAsync(manager);
        var theirs = await SaleAsync(manager);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [Tool("tu_1", AssistantPrompt.CreateDraft, new
        {
            kind = "whatsapp", purpose = "follow_up", customerId = mine.Customer.Id, subject = "", body = $"Please check {theirs.Invoice.Number}.",
        })]);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("Done.")]);

        var draft = (await SendAsync(rep, "Draft a WhatsApp follow-up")).Single(e => e.Event == "draft").Data.Deserialize<AssistantDraftDto>(TestClients.Json)!;

        Assert.Equal(AiDraftKind.WhatsApp, draft.Kind);
        Assert.Null(draft.Subject);
        Assert.Equal($"{theirs.Invoice.Number} belongs to {theirs.Customer.Name}, not {mine.Customer.Name}.", Assert.Single(draft.Warnings));
    }

    [Fact]
    public async Task Conversations_are_private_to_the_user_who_started_them()
    {
        var (app, claude) = WithFakeClaude();
        var (owner, _) = await app.CreateClientAsync(Roles.SalesRep);
        var (admin, _) = await app.CreateClientAsync(Roles.Admin);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("Hello.")]);
        var id = ConversationId(await SendAsync(owner, "Hi"));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/assistant/conversations/{id}", Ct)).StatusCode);
        Assert.DoesNotContain(await (await admin.GetAsync("/api/assistant/conversations", Ct)).ReadAsync<List<AssistantConversationSummaryDto>>(), c => c.Id == id);
        var post = await admin.PostAsJsonAsync("/api/assistant/messages", new AssistantSendRequest { ConversationId = id, Message = "Hi" }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode); // a normal error response, before any streaming
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/assistant/conversations/{id}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/assistant/conversations/{id}", Ct)).StatusCode);
        Assert.Empty(await MessagesAsync(id));
    }

    [Fact]
    public async Task Saved_messages_cannot_be_changed()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("Hello.")]);
        var id = ConversationId(await SendAsync(rep, "Hi"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE AiMessages SET ContentJson = N'[]' WHERE ConversationId = {id}", Ct));
        Assert.Equal(51000, error.Number);
    }

    [Fact]
    public async Task A_refusal_is_reported_and_the_conversation_stays_usable()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => throw new AiRefusedException("Claude declined to process this content."));

        var events = await SendAsync(rep, "Something Claude won't do");

        Assert.Equal("error", events[^1].Event);
        Assert.Equal("Claude declined to answer that message.", events[^1].Data.GetProperty("message").GetString());
        var id = ConversationId(events);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("Happy to help with that.")]);
        Assert.Equal("Happy to help with that.", Text(await SendAsync(rep, "Who are our top customers?", id)));
        // The unanswered question stays in the history; the API joins consecutive user messages.
        Assert.Equal(2, claude.Conversations[^1].Messages.Count);
    }

    [Fact]
    public async Task A_long_conversation_asks_the_user_to_start_a_new_one()
    {
        var (app, claude) = WithFakeClaude(("Ai:Assistant:MaxContextTokens", "10000"));
        claude.TurnUsage = new ClaudeUsage("claude-opus-5-5", 10, 200, 11_000, 0, 0.01m, 900);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [new ClaudeTextBlock("A reply.")]);
        var events = await SendAsync(rep, "Hi");
        Assert.Contains("length limit", events[^1].Data.GetProperty("notice").GetString(), StringComparison.Ordinal);

        var response = await rep.PostAsJsonAsync("/api/assistant/messages", new AssistantSendRequest { ConversationId = ConversationId(events), Message = "More" }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Without_an_api_key_the_assistant_is_refused_up_front()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var response = await rep.PostAsJsonAsync("/api/assistant/messages", new AssistantSendRequest { Message = "Hi" }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
