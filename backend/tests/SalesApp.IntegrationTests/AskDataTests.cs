using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI.AskData;
using SalesApp.AI.Claude;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class AskDataTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] FollowUps = ["And last year?"];

    private (WebApplicationFactory<Program> App, FakeClaudeGateway Claude) WithFakeClaude()
    {
        var claude = new FakeClaudeGateway();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IClaudeGateway>(claude)));
        return (app, claude);
    }

    private static ClaudeToolUseBlock Sql(string sql, string id = "tu_sql") => new(id, AskDataPrompt.RunSqlTool, new Dictionary<string, JsonElement>
    {
        ["sql"] = JsonSerializer.SerializeToElement(sql),
        ["purpose"] = JsonSerializer.SerializeToElement("Test query"),
    });

    private static ClaudeToolUseBlock Answer(string summary, string resultId, string chartType = "none", string chartX = "", string chartY = "") =>
        new("tu_answer", AskDataPrompt.SubmitAnswerTool, new Dictionary<string, JsonElement>
        {
            ["summary"] = JsonSerializer.SerializeToElement(summary),
            ["resultId"] = JsonSerializer.SerializeToElement(resultId),
            ["chartType"] = JsonSerializer.SerializeToElement(chartType),
            ["chartX"] = JsonSerializer.SerializeToElement(chartX),
            ["chartY"] = JsonSerializer.SerializeToElement(chartY),
            ["followUps"] = JsonSerializer.SerializeToElement(FollowUps),
        });

    /// <summary>Posts a question and reads the whole event stream into (event, data) pairs.</summary>
    private static async Task<List<(string Event, JsonElement Data)>> AskAsync(HttpClient client, string question)
    {
        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest { Question = question }, TestClients.Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = new List<(string, JsonElement)>();
        foreach (var frame in (await response.Content.ReadAsStringAsync(Ct)).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = frame.Split('\n');
            var name = lines.First(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
            var data = lines.First(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
            events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }

        return events;
    }

    private static AskAnswerDto AnswerOf(List<(string Event, JsonElement Data)> events) =>
        events.Single(e => e.Event == "answer").Data.Deserialize<AskAnswerDto>(TestClients.Json)!;

    private async Task<List<AiQueryLog>> QueryLogAsync(Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiQueryLogs.Where(l => l.RunId == runId).ToListAsync(Ct);
    }

    [Fact]
    public async Task A_question_is_answered_from_rows_the_database_returned()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateStockedProductAsync(manager, 100m, listPrice: 400m);
        var customer = await TestData.CreateCustomerAsync(manager);
        var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (product.Id, 5m)));
        var invoice = await TestData.InvoiceAsync(manager, order.Id);
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);

        var sql = $"SELECT CustomerName AS Customer, SUM(TaxableValue) AS SalesValue, COUNT(DISTINCT InvoiceId) AS Invoices FROM rpt.SalesLines WHERE CustomerId = {customer.Id} GROUP BY CustomerName";
        claude.Turns.Enqueue(_ => [new ClaudeThinkingBlock("Sum the sales lines.", "sig-1"), Sql(sql)]);
        claude.Turns.Enqueue(_ => [Answer("The customer bought goods worth 2,000.", "r1", "bar", "Customer", "SalesValue")]);

        var events = await AskAsync(rep, "How much did this customer buy?");

        var steps = events.Where(e => e.Event == "step").Select(e => e.Data.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["Thinking", "Query", "Rows", "Thinking"], steps);
        var answer = AnswerOf(events);
        Assert.Equal("The customer bought goods worth 2,000.", answer.Summary);
        Assert.Equal(sql, answer.Sql);
        Assert.Equal(["Customer", "SalesValue", "Invoices"], answer.Columns.Select(c => c.Name));
        Assert.Equal(["Customer", "Sales value", "Invoices"], answer.Columns.Select(c => c.Label));
        Assert.Equal([AskColumnKind.Text, AskColumnKind.Money, AskColumnKind.Count], answer.Columns.Select(c => c.Kind));
        var row = Assert.Single(answer.Rows);
        Assert.Equal(customer.Name, ((JsonElement)row[0]!).GetString());
        Assert.Equal(invoice.TaxableValue, ((JsonElement)row[1]!).GetDecimal()); // straight from the database
        Assert.Equal(new AskChartDto(AskChartType.Bar, "Customer", "SalesValue"), answer.Chart);
        Assert.Equal(["And last year?"], answer.FollowUps);
        Assert.Equal(1, answer.QueriesRun);
        Assert.NotNull(answer.ResultId);

        // Claude got a resultId and the rows back, and its thinking was passed back unchanged with the signature.
        var second = claude.Conversations[1];
        var toolResult = Assert.IsType<ClaudeToolResultBlock>(Assert.Single(second.Messages[^1].Blocks));
        Assert.False(toolResult.IsError);
        Assert.Contains("\"resultId\":\"r1\"", toolResult.Content, StringComparison.Ordinal);
        Assert.Contains(second.Messages[1].Blocks, b => b is ClaudeThinkingBlock { Signature: "sig-1" });
        Assert.True(second.System[^1].CacheAfter);
        Assert.DoesNotContain("rpt_mgr", second.System[^1].Text, StringComparison.Ordinal); // reps aren't told about margins

        var log = Assert.Single(await QueryLogAsync(answer.RunId));
        Assert.Equal(AiQueryOutcome.Succeeded, log.Outcome);
        Assert.Equal(ReadOnlySqlExecutor.Reader, log.RunAs);
        Assert.Equal(1, log.RowCount);

        var excel = await rep.GetAsync($"/api/ask/results/{answer.ResultId}/excel", Ct);
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        var (other, _) = await app.CreateClientAsync(Roles.SalesRep);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/ask/results/{answer.ResultId}/excel", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_query_outside_the_views_is_refused_and_claude_is_told_why()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [Sql("SELECT PasswordHash FROM dbo.AspNetUsers")]);
        claude.Turns.Enqueue(_ => [Answer("I can't see user accounts.", "")]);

        var events = await AskAsync(rep, "Show me everyone's password");

        var refusal = events.Single(e => e.Event == "step" && e.Data.GetProperty("kind").GetString() == "Rejected");
        Assert.Contains("dbo.AspNetUsers isn't available", refusal.Data.GetProperty("message").GetString(), StringComparison.Ordinal);
        var toolResult = Assert.IsType<ClaudeToolResultBlock>(Assert.Single(claude.Conversations[1].Messages[^1].Blocks));
        Assert.True(toolResult.IsError);
        var answer = AnswerOf(events);
        Assert.Null(answer.ResultId);
        Assert.Empty(answer.Rows);
        Assert.Equal(AiQueryOutcome.Rejected, Assert.Single(await QueryLogAsync(answer.RunId)).Outcome);
    }

    [Fact]
    public async Task Margins_are_for_managers_only()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        const string sql = "SELECT COUNT(*) AS Lines FROM rpt_mgr.Margins";

        claude.Turns.Enqueue(_ => [Sql(sql)]);
        claude.Turns.Enqueue(_ => [Answer("Not available.", "")]);
        var repAnswer = AnswerOf(await AskAsync(rep, "What's our margin?"));
        Assert.Equal(AiQueryOutcome.Rejected, Assert.Single(await QueryLogAsync(repAnswer.RunId)).Outcome);

        claude.Turns.Enqueue(conversation =>
        {
            Assert.Contains("rpt_mgr.Margins", conversation.System[^1].Text, StringComparison.Ordinal);
            return [Sql(sql)];
        });
        claude.Turns.Enqueue(_ => [Answer("Counted.", "r1")]);
        var managerAnswer = AnswerOf(await AskAsync(manager, "What's our margin?"));
        var log = Assert.Single(await QueryLogAsync(managerAnswer.RunId));
        Assert.Equal(AiQueryOutcome.Succeeded, log.Outcome);
        Assert.Equal(ReadOnlySqlExecutor.ManagerReader, log.RunAs);
    }

    [Fact]
    public async Task A_database_error_goes_back_to_claude_to_fix()
    {
        var (app, claude) = WithFakeClaude();
        var (rep, _) = await app.CreateClientAsync(Roles.SalesRep);
        claude.Turns.Enqueue(_ => [Sql("SELECT NoSuchColumn FROM rpt.Customers")]);
        claude.Turns.Enqueue(_ => [Sql("SELECT COUNT(*) AS Customers FROM rpt.Customers", "tu_sql2")]);
        claude.Turns.Enqueue(_ => [Answer("Counted the customers.", "r1")]);

        var events = await AskAsync(rep, "How many customers?");

        var firstResult = Assert.IsType<ClaudeToolResultBlock>(Assert.Single(claude.Conversations[1].Messages[^1].Blocks));
        Assert.True(firstResult.IsError);
        Assert.Contains("Invalid column name", firstResult.Content, StringComparison.Ordinal);
        var answer = AnswerOf(events);
        Assert.Equal(2, answer.QueriesRun);
        Assert.Single(answer.Rows);
        Assert.Equal([AiQueryOutcome.Failed, AiQueryOutcome.Succeeded], (await QueryLogAsync(answer.RunId)).OrderBy(l => l.Id).Select(l => l.Outcome));
    }

    [Fact]
    public async Task Without_an_api_key_asking_is_refused_up_front()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        var response = await rep.PostAsJsonAsync("/api/ask", new AskRequest { Question = "Top customers?" }, TestClients.Json, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE rpt.Products SET ListPrice = 0")]
    [InlineData("DELETE FROM rpt.Invoices")]
    [InlineData("SELECT TOP 1 PasswordHash FROM dbo.AspNetUsers")]
    [InlineData("SELECT TOP 1 * FROM dbo.Customers")]
    [InlineData("REVERT; SELECT TOP 1 * FROM dbo.Customers")]
    [InlineData("EXECUTE AS LOGIN = 'sa'; SELECT 1")]
    [InlineData("SELECT TOP 1 CostValue FROM rpt_mgr.Margins")]
    [InlineData("CREATE TABLE dbo.Leak (x int)")]
    public async Task The_database_itself_refuses_what_the_checker_would_have_stopped(string sql)
    {
        // Bypasses SqlGuard on purpose: even if the checker missed something, the reporting user can't do it.
        var executor = factory.Services.GetRequiredService<IReadOnlySqlExecutor>();

        await Assert.ThrowsAsync<SqlException>(() => executor.RunAsync(sql, ReadOnlySqlExecutor.Reader, Ct));
    }

    [Fact]
    public async Task Queries_run_under_snapshot_isolation_as_the_reporting_user()
    {
        var executor = factory.Services.GetRequiredService<IReadOnlySqlExecutor>();

        var result = await executor.RunAsync(
            "SELECT USER_NAME() AS RunAs, CASE transaction_isolation_level WHEN 5 THEN 'Snapshot' ELSE 'Other' END AS Isolation FROM sys.dm_exec_sessions WHERE session_id = @@SPID",
            ReadOnlySqlExecutor.ManagerReader, Ct);

        var row = Assert.Single(result.Rows);
        Assert.Equal("ai_reader_mgr", row[0]);
        Assert.Equal("Snapshot", row[1]);
    }

    [Fact]
    public async Task Every_view_column_is_described_to_claude()
    {
        // A column added to a view but left out of the description would be invisible to Claude.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columns = await db.Database.SqlQueryRaw<ViewColumn>(
            "SELECT s.name AS SchemaName, v.name AS ViewName, c.name AS ColumnName FROM sys.views v JOIN sys.schemas s ON s.schema_id = v.schema_id JOIN sys.columns c ON c.object_id = v.object_id WHERE s.name IN ('rpt', 'rpt_mgr')")
            .ToListAsync(Ct);
        var description = AskDataPrompt.Views + AskDataPrompt.ManagerViews;

        Assert.NotEmpty(columns);
        foreach (var view in columns.GroupBy(c => $"{c.SchemaName}.{c.ViewName}"))
        {
            var start = description.IndexOf(view.Key + ":", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{view.Key} isn't described");
            var end = description.IndexOf("\n\n", start, StringComparison.Ordinal);
            var section = end < 0 ? description[start..] : description[start..end];
            Assert.All(view, c => Assert.True(
                System.Text.RegularExpressions.Regex.IsMatch(section, $@"\b{c.ColumnName}\b"), $"{view.Key}.{c.ColumnName} isn't described"));
        }
    }

    private sealed record ViewColumn(string SchemaName, string ViewName, string ColumnName);
}
