using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.IntegrationTests;

public sealed class AiUsageTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SeedAsync(params AiUsageLog[] logs)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiUsageLogs.AddRange(logs);
        await db.SaveChangesAsync(Ct);
    }

    private static AiUsageLog Log(string feature, Guid? user, decimal cost, AiOutcome outcome = AiOutcome.Success, int cacheRead = 0, int hoursAgo = 1) => new()
    {
        Feature = feature,
        Model = "claude-opus-5-5",
        UserId = user,
        CreatedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo),
        DurationMs = 4000,
        InputTokens = 100,
        OutputTokens = 500,
        CacheReadTokens = cacheRead,
        CacheWriteTokens = 0,
        CostUsd = cost,
        Outcome = outcome,
        Error = outcome == AiOutcome.Success ? null : "Stop reason: refusal",
    };

    [Fact]
    public async Task Administrators_see_spend_by_feature_and_user_including_background_work()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var (_, repId) = await factory.CreateClientAsync(Roles.SalesRep);
        var feature = $"Test{Guid.NewGuid():N}"[..20];
        await SeedAsync(
            Log(feature, repId, 0.50m, cacheRead: 900),
            Log(feature, repId, 0.25m, AiOutcome.Refused),
            Log(feature, null, 0.10m, hoursAgo: 30));

        var summary = await (await admin.GetAsync("/api/admin/ai-usage/summary?days=7", Ct)).ReadAsync<AiUsageSummaryDto>();

        Assert.Equal(7, summary.Days);
        Assert.Equal(7, summary.Daily.Count);
        var mine = Assert.Single(summary.ByFeature, f => f.Feature == feature);
        Assert.Equal(3, mine.Calls);
        Assert.Equal(1, mine.Failed);
        Assert.Equal(Math.Round(0.85m * 88, 2), mine.CostInr);
        var rep = Assert.Single(summary.ByUser, u => u.UserId == repId);
        Assert.Equal("Test SalesRep", rep.Name);
        Assert.Equal(Math.Round(0.75m * 88, 2), rep.Last24HoursInr);
        Assert.Equal(37.5m, rep.BudgetUsedPercent); // $0.75 of the $2 daily budget
        Assert.Contains(summary.ByUser, u => u.UserId == null && u.Name.StartsWith("Background", StringComparison.Ordinal));
        Assert.True(summary.Refused >= 1);
        Assert.True(summary.CacheReadPercent > 0);
    }

    [Fact]
    public async Task Calls_can_be_filtered_to_problems()
    {
        var (admin, _) = await factory.CreateClientAsync(Roles.Admin);
        var feature = $"Test{Guid.NewGuid():N}"[..20];
        await SeedAsync(Log(feature, null, 0.01m), Log(feature, null, 0.02m, AiOutcome.Failed));

        var all = await (await admin.GetAsync($"/api/admin/ai-usage/calls?feature={feature}", Ct)).ReadAsync<PagedResult<AiCallDto>>();
        var problems = await (await admin.GetAsync($"/api/admin/ai-usage/calls?feature={feature}&problemsOnly=true", Ct)).ReadAsync<PagedResult<AiCallDto>>();

        Assert.Equal(2, all.TotalCount);
        var failed = Assert.Single(problems.Items);
        Assert.Equal(AiOutcome.Failed, failed.Outcome);
        Assert.Equal("Background (morning insights)", failed.UserName);
        Assert.Equal(Math.Round(0.02m * 88, 2), failed.CostInr);
    }

    [Fact]
    public async Task The_sql_query_log_shows_what_the_checker_refused()
    {
        var (admin, adminId) = await factory.CreateClientAsync(Roles.Admin);
        var marker = $"marker{Guid.NewGuid():N}"[..20];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiQueryLogs.AddRange(
                new AiQueryLog { RunId = Guid.NewGuid(), UserId = adminId, Question = $"Top customers {marker}", Sql = "SELECT 1", RunAs = "ai_reader", Outcome = AiQueryOutcome.Succeeded, RowCount = 1, CreatedAtUtc = DateTime.UtcNow },
                new AiQueryLog { RunId = Guid.NewGuid(), UserId = adminId, Question = $"Delete everything {marker}", Sql = "DELETE FROM rpt.Invoices", RunAs = "ai_reader", Outcome = AiQueryOutcome.Rejected, Error = "Only a single SELECT statement is allowed.", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        var rejected = await (await admin.GetAsync($"/api/admin/ai-usage/queries?q={marker}&outcome=Rejected", Ct)).ReadAsync<PagedResult<AiQueryDto>>();

        var query = Assert.Single(rejected.Items);
        Assert.Equal("DELETE FROM rpt.Invoices", query.Sql);
        Assert.Equal("Only a single SELECT statement is allowed.", query.Error);
        Assert.Equal("Test Admin", query.UserName);
    }

    [Fact]
    public async Task Only_administrators_can_see_ai_usage()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/ai-usage/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/ai-usage/calls", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/ai-usage/queries", Ct)).StatusCode);
    }
}
