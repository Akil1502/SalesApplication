using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI;
using SalesApp.AI.AskData;
using SalesApp.AI.OrderEntry;
using SalesApp.Core.Common;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Evals;

/// <param name="Detail">For a failed Ask-your-data case, the SQL behind the answer.</param>
/// <param name="Summary">For a failed Ask-your-data case, the answer's written summary.</param>
public sealed record EvalResult(string Id, string Description, bool Passed, string Reason, decimal CostUsd, string? Detail = null, string? Summary = null);

/// <summary>Writes a run's results as a markdown report under results/ (not committed) and returns its path.</summary>
public static class EvalReport
{
    public static string Write(string suite, IReadOnlyList<EvalResult> results, TimeSpan elapsed, decimal usdToInr)
    {
        var folder = Path.Combine(ProjectFolder(), "results");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{suite}-{DateTime.Now:yyyyMMdd-HHmm}.md");
        var passed = results.Count(r => r.Passed);
        var cost = results.Sum(r => r.CostUsd);
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# {suite} eval, {DateTime.Now:d MMM yyyy HH:mm}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"**{passed} of {results.Count} passed** ({(results.Count == 0 ? 0 : passed * 100 / results.Count)}%). ")
            .AppendLine(CultureInfo.InvariantCulture, $"Estimated cost ${cost:0.000} (about ₹{cost * usdToInr:0}), {elapsed.TotalMinutes:0.0} minutes.")
            .AppendLine()
            .AppendLine("| Case | Result | Why | Cost (₹) |")
            .AppendLine("|---|---|---|---:|");
        foreach (var r in results)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {r.Id}: {r.Description} | {(r.Passed ? "pass" : "**fail**")} | {r.Reason.Replace("|", "/", StringComparison.Ordinal)} | {r.CostUsd * usdToInr:0.00} |");
        }

        foreach (var r in results.Where(r => r.Detail is not null || r.Summary is not null))
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {r.Id}");
            if (r.Summary is not null)
            {
                text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"> {r.Summary.ReplaceLineEndings(" ")}");
            }

            if (r.Detail is not null)
            {
                text.AppendLine().AppendLine("```sql").AppendLine(r.Detail).AppendLine("```");
            }
        }

        File.WriteAllText(path, text.ToString());
        return path;
    }

    private static string ProjectFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !folder.EnumerateFiles("*.csproj").Any())
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? AppContext.BaseDirectory;
    }
}

/// <summary>Ask your data, end to end against Claude: does each answer hold the same values as the reference query?</summary>
public sealed class AskDataEvals(EvalHost host) : IClassFixture<EvalHost>
{
    [Fact]
    public async Task Answers_match_the_reference_queries()
    {
        Assert.SkipUnless(EvalSettings.Enabled, EvalSettings.SkipReason);
        var cases = AskDataCases.All.Where(c => EvalSettings.Selected(c.Id)).ToList();
        Assert.SkipWhen(cases.Count == 0, "None of the cases in SALESAPP_EVAL_CASES are in this suite.");
        Assert.True(host.ClaudeAvailable, "No Claude API key: set Anthropic:ApiKey in user-secrets for SalesApp.Api (see the README).");

        var ct = TestContext.Current.CancellationToken;
        var started = Stopwatch.StartNew();
        var results = new List<EvalResult>();
        foreach (var testCase in cases)
        {
            await using var scope = host.Scope();
            var services = scope.ServiceProvider;
            var gold = await services.GetRequiredService<IReadOnlySqlExecutor>()
                .RunAsync(testCase.Sql(services.GetRequiredService<IBusinessClock>().Today), ReadOnlySqlExecutor.ManagerReader, ct);
            try
            {
                var answer = await services.GetRequiredService<IAskDataService>().AskAsync(testCase.Question, _ => Task.CompletedTask, ct);
                var required = testCase.Required ?? [.. Enumerable.Range(0, gold.Columns.Count).Select(i => new[] { i })];
                var (passed, reason) = answer.ResultId is null
                    ? (false, $"no result chosen: {answer.Summary}")
                    : ResultGrader.Grade(gold.Rows, answer.Rows, required, answer.Summary);
                results.Add(new EvalResult(testCase.Id, testCase.Question, passed, reason, answer.Usage.CostUsd,
                    passed ? null : answer.Sql, passed ? null : answer.Summary));
            }
            catch (AiBudgetExceededException)
            {
                results.Add(new EvalResult(testCase.Id, testCase.Question, false, $"stopped: the ${EvalSettings.BudgetUsd} budget for the run was reached", 0));
                break;
            }
            catch (Exception exception) when (exception is AiServiceException or AiRefusedException)
            {
                results.Add(new EvalResult(testCase.Id, testCase.Question, false, exception.Message, 0));
            }
        }

        await FinishAsync("ask-data", results, started.Elapsed);
    }

    private async Task FinishAsync(string suite, List<EvalResult> results, TimeSpan elapsed)
    {
        var (cost, calls) = await host.SpendAsync();
        var path = EvalReport.Write(suite, results, elapsed, 88m);
        var passed = results.Count(r => r.Passed);
        TestContext.Current.TestOutputHelper?.WriteLine($"{passed}/{results.Count} passed, ${cost:0.000} over {calls} Claude calls. Report: {path}");
        Assert.True(passed >= results.Count * 0.8, $"Only {passed} of {results.Count} passed. See {path}");
    }
}

/// <summary>Smart order entry, end to end against Claude: are the right products and quantities read?</summary>
public sealed class OrderEntryEvals(EvalHost host) : IClassFixture<EvalHost>
{
    [Fact]
    public async Task Orders_are_read_correctly()
    {
        Assert.SkipUnless(EvalSettings.Enabled, EvalSettings.SkipReason);
        var cases = OrderCases.All.Where(c => EvalSettings.Selected(c.Id)).ToList();
        Assert.SkipWhen(cases.Count == 0, "None of the cases in SALESAPP_EVAL_CASES are in this suite.");
        Assert.True(host.ClaudeAvailable, "No Claude API key: set Anthropic:ApiKey in user-secrets for SalesApp.Api (see the README).");

        var ct = TestContext.Current.CancellationToken;
        var started = Stopwatch.StartNew();
        var results = new List<EvalResult>();
        foreach (var testCase in cases)
        {
            await using var scope = host.Scope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AppDbContext>();
            var customerId = await db.Customers.Where(c => c.IsActive && (testCase.Customer == null || c.Name == testCase.Customer))
                .OrderBy(c => c.Code).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)
                ?? await db.Customers.Where(c => c.IsActive).OrderBy(c => c.Code).Select(c => c.Id).FirstAsync(ct);
            var file = testCase.File is null ? null : await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Samples", testCase.File), ct);

            try
            {
                var read = await services.GetRequiredService<IOrderExtractionService>()
                    .ExtractAsync(new OrderExtractionInput(testCase.Text, file, customerId), ct);
                var (passed, reason) = testCase.IsOrder
                    ? OrderGrader.Grade(testCase.Expected, [.. read.Lines.Select(l => (l.Product?.Sku, l.Quantity))], testCase.Forbidden ?? [])
                    : read.Lines.Count == 0 ? (true, "recognised as not an order") : (false, $"read {read.Lines.Count} lines from a message that isn't an order");
                results.Add(new EvalResult(testCase.Id, testCase.Kind, passed, reason, read.Usage.CostUsd));
            }
            catch (AiBudgetExceededException)
            {
                results.Add(new EvalResult(testCase.Id, testCase.Kind, false, $"stopped: the ${EvalSettings.BudgetUsd} budget for the run was reached", 0));
                break;
            }
            catch (Exception exception) when (exception is AiServiceException or AiRefusedException)
            {
                results.Add(new EvalResult(testCase.Id, testCase.Kind, false, exception.Message, 0));
            }
        }

        var (cost, calls) = await host.SpendAsync();
        var path = EvalReport.Write("order-entry", results, started.Elapsed, 88m);
        var passedCount = results.Count(r => r.Passed);
        TestContext.Current.TestOutputHelper?.WriteLine($"{passedCount}/{results.Count} passed, ${cost:0.000} over {calls} Claude calls. Report: {path}");
        Assert.True(passedCount >= results.Count * 0.8, $"Only {passedCount} of {results.Count} passed. See {path}");
    }
}
