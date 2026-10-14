using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SalesApp.AI.Claude;
using SalesApp.AI.Insights;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Insights;
using SalesApp.Core.Entities;
using SalesApp.Core.Forecasting;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

/// <summary>Insights over 14 months of generated history, with its planted customers, stock-outs and bulk order.</summary>
public sealed class InsightsTests(DemoDataFactory factory) : IClassFixture<DemoDataFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (WebApplicationFactory<Program> App, FakeClaudeGateway Claude) WithFakeClaude()
    {
        var claude = new FakeClaudeGateway();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IClaudeGateway>(claude)));
        return (app, claude);
    }

    private static async Task<InsightsDto> RefreshAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/insights/refresh", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<InsightsDto>();
    }

    /// <summary>The metrics Claude was given, read back out of the request.</summary>
    private static InsightMetricsDto MetricsIn(ClaudeRequest request)
    {
        var text = request.System[^1].Text;
        var json = text[(text.IndexOf("<metrics>", StringComparison.Ordinal) + "<metrics>".Length)..text.IndexOf("</metrics>", StringComparison.Ordinal)];
        return JsonSerializer.Deserialize<InsightMetricsDto>(json, TestClients.Json)!;
    }

    private static string Rupees(decimal value) => "₹" + value.ToString("#,##0", CultureInfo.GetCultureInfo("en-IN"));

    [Fact]
    public async Task Without_ai_every_figure_is_still_computed_from_the_data()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var insights = await RefreshAsync(manager);

        Assert.Equal(NarrativeStatus.AiOff, insights.NarrativeStatus);
        Assert.Null(insights.Narrative);
        var metrics = insights.Metrics;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IBusinessClock>().Today;
        var yearStart = FinancialYear.Start(today);
        var sales = await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= yearStart && i.InvoiceDate <= today).SumAsync(i => i.TaxableValue, Ct);
        Assert.Equal(Math.Round(sales, 0, MidpointRounding.AwayFromZero), metrics.Sales.FinancialYearToDate);

        // Thirteen full months: too short to backtest, so same month last year is used, for this month and the next two.
        Assert.Equal(ForecastMethod.SeasonalNaive, metrics.Forecast.Method);
        Assert.Equal([new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1), new DateOnly(today.Year, today.Month, 1).AddMonths(2)],
            metrics.Forecast.Next.Select(n => n.Month));
        Assert.All(metrics.Forecast.Next, n => Assert.True(n.Low <= n.Forecast && n.Forecast <= n.High));
        Assert.DoesNotContain(metrics.Forecast.History, h => h.Month >= new DateOnly(today.Year, today.Month, 1)); // full months only

        // The three products whose suppliers are late right now.
        Assert.Contains("GT-VST", metrics.Reorder.Select(r => r.Sku));
        Assert.Contains("HT-TWL", metrics.Reorder.Select(r => r.Sku));
        Assert.Contains("PC-40", metrics.Reorder.Select(r => r.Sku));
        var vests = metrics.Reorder.Single(r => r.Sku == "GT-VST");
        Assert.True(vests.RunsOutBeforeDelivery);
        Assert.True(vests.SuggestedQuantity > 0);

        // The one-off bulk order shows as an unusual invoice, and GT-VST's empty shelf as a stock-out.
        var bulk = await db.SalesOrders.Where(o => o.Notes == "Bulk order for export programme")
            .Join(db.Invoices, o => o.InvoiceId, i => i.Id, (o, i) => i.Number).SingleAsync(Ct);
        var unusual = Assert.Single(metrics.Anomalies, a => a.Kind == AnomalyKind.UnusualInvoice && a.Reference == bulk);
        Assert.True(unusual.Ratio >= 4);
        Assert.Contains(metrics.Anomalies, a => a.Kind == AnomalyKind.Stockout && a.Title.StartsWith("GT-VST has been out of stock", StringComparison.Ordinal));
        Assert.Contains(metrics.Anomalies, a => a.Kind == AnomalyKind.StoppedBuying);
        Assert.All(metrics.Anomalies, a => Assert.False(string.IsNullOrWhiteSpace(a.Detail)));
    }

    [Fact]
    public async Task A_briefing_whose_figures_all_check_out_is_kept()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        claude.Reply = request =>
        {
            var m = MetricsIn(request);
            return new
            {
                headline = $"Sales this year are {Rupees(m.Sales.FinancialYearToDate)}",
                summary = $"{m.Receivables.OverdueInvoices} invoices are overdue, worth {Rupees(m.Receivables.Overdue)} in all.",
                cards = new[]
                {
                    new { kind = "risk", title = "Overdue money", body = $"Chase the {m.Receivables.CustomersOverdue} customers with overdue invoices." },
                    new { kind = "action", title = "Reorder vests", body = "GT-VST is out of stock; reorder it today." },
                },
            };
        };

        var insights = await RefreshAsync(manager);

        Assert.Equal(NarrativeStatus.Verified, insights.NarrativeStatus);
        Assert.Null(insights.NarrativeNote);
        Assert.Equal(2, insights.Narrative!.Cards.Count);
        Assert.Equal(InsightCardKind.Risk, insights.Narrative.Cards[0].Kind);
        Assert.Single(claude.Requests); // no retry needed
        Assert.True(insights.CostInr > 0);
        Assert.True(claude.Requests[0].System[^1].CacheAfter); // the metrics are a cached prefix for a retry
        Assert.NotNull(claude.Requests[0].OutputSchema);

        // The page shows the same run afterwards.
        var latest = await (await manager.GetAsync("/api/insights", Ct)).ReadAsync<InsightsDto>();
        Assert.Equal(insights.RunId, latest.RunId);
        Assert.Equal(insights.Narrative.Headline, latest.Narrative!.Headline);
    }

    [Fact]
    public async Task Invented_figures_get_one_retry_then_whatever_still_does_not_check_out_is_removed()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        claude.Reply = request =>
        {
            var m = MetricsIn(request);
            return new
            {
                headline = "Sales are up 987.6% this year",
                summary = $"Sales this year are {Rupees(m.Sales.FinancialYearToDate)}.",
                cards = new[]
                {
                    new { kind = "opportunity", title = "A big win", body = "Murugan Garments will order ₹12,34,567 next week." },
                    new { kind = "info", title = "Overdue", body = $"{Rupees(m.Receivables.Overdue)} is overdue." },
                },
            };
        };

        var insights = await RefreshAsync(manager);

        Assert.Equal(2, claude.Requests.Count);
        var retry = Assert.IsType<ClaudeText>(Assert.Single(claude.Requests[1].Content)).Text;
        Assert.Contains("987.6%", retry, StringComparison.Ordinal);
        Assert.Contains("₹12,34,567", retry, StringComparison.Ordinal);

        Assert.Equal(NarrativeStatus.Trimmed, insights.NarrativeStatus);
        var narrative = insights.Narrative!;
        Assert.Equal(InsightsService.DefaultHeadline(insights.Metrics), narrative.Headline); // replaced with a computed one
        Assert.StartsWith("Sales this year are ₹", narrative.Summary, StringComparison.Ordinal);
        Assert.Equal("Overdue", Assert.Single(narrative.Cards).Title);
        Assert.Contains("987.6%", insights.NarrativeNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retry_that_fixes_the_figures_is_verified()
    {
        var (app, claude) = WithFakeClaude();
        var (manager, _) = await app.CreateClientAsync(Roles.SalesManager);
        claude.Reply = request =>
        {
            var m = MetricsIn(request);
            var first = request.Content is [ClaudeText { Text: var text }] && text == InsightNarrativePrompt.Request;
            return new
            {
                headline = first ? "Overdue money tripled to ₹99 crore" : $"{Rupees(m.Receivables.Overdue)} is overdue",
                summary = "Chase the overdue invoices this week.",
                cards = new[] { new { kind = "action", title = "Chase payments", body = "Start with the oldest overdue invoices." } },
            };
        };

        var insights = await RefreshAsync(manager);

        Assert.Equal(2, claude.Requests.Count);
        Assert.Equal(NarrativeStatus.Verified, insights.NarrativeStatus);
        Assert.EndsWith("is overdue", insights.Narrative!.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sales_reps_cannot_see_insights()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync("/api/insights", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PostAsync("/api/insights/refresh", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_nightly_run_happens_once_a_day_as_the_system()
    {
        await using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Insights:NightlyEnabled", "true");
            builder.UseSetting("Ai:Insights:NightlyAt", "00:00");
        });
        var scheduler = app.Services.GetServices<IHostedService>().OfType<InsightsScheduler>().Single();
        using (var scope = app.Services.CreateScope())
        {
            // Any run already made today (by another test) counts as today's; start from a clean slate.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InsightRuns.ExecuteDeleteAsync(Ct);
        }

        Assert.True(await scheduler.RunIfDueAsync(Ct));
        Assert.False(await scheduler.RunIfDueAsync(Ct));

        using var check = app.Services.CreateScope();
        var run = Assert.Single(await check.ServiceProvider.GetRequiredService<AppDbContext>().InsightRuns.ToListAsync(Ct));
        Assert.Equal(InsightTrigger.Nightly, run.Trigger);
        Assert.Null(run.RequestedByUserId);
    }
}
