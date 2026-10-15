using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.AskData;

/// <summary>What happened to one AI-written query: its rows, or why it was refused or failed.</summary>
public sealed record GuardedQueryOutcome(AiQueryOutcome Outcome, QueryResult? Result, string? Problem);

public interface IGuardedQueryRunner
{
    /// <summary>The database user the current user's queries run as (managers can also read the margins view).</summary>
    string RunAs { get; }

    Task<GuardedQueryOutcome> RunAsync(Guid runId, string question, string sql, CancellationToken cancellationToken);
}

/// <summary>
/// The one path for SQL written by Claude, shared by Ask your data and the assistant: <see cref="SqlGuard"/> checks
/// it, <see cref="IReadOnlySqlExecutor"/> runs it as a reporting-only database user, and every attempt is logged to
/// <see cref="AiQueryLog"/>. Errors come back as text Claude can act on.
/// </summary>
public sealed class GuardedQueryRunner(
    IReadOnlySqlExecutor executor,
    AppDbContext db,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<GuardedQueryRunner> logger) : IGuardedQueryRunner
{
    public string RunAs => currentUser.Has(Permissions.CostPricesView) ? ReadOnlySqlExecutor.ManagerReader : ReadOnlySqlExecutor.Reader;

    private string[] Schemas => currentUser.Has(Permissions.CostPricesView) ? ["rpt", "rpt_mgr"] : ["rpt"];

    public async Task<GuardedQueryOutcome> RunAsync(Guid runId, string question, string sql, CancellationToken cancellationToken)
    {
        var runAs = RunAs;
        var check = SqlGuard.Check(sql, Schemas);
        if (!check.Allowed)
        {
            await LogAsync(runId, question, sql, runAs, AiQueryOutcome.Rejected, check.Problem, 0, 0);
            return new GuardedQueryOutcome(AiQueryOutcome.Rejected, null, check.Problem);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await executor.RunAsync(sql, runAs, cancellationToken);
            await LogAsync(runId, question, sql, runAs, AiQueryOutcome.Succeeded, null, result.Rows.Count, result.DurationMs);
            return new GuardedQueryOutcome(AiQueryOutcome.Succeeded, result, null);
        }
        catch (Exception exception) when (exception is SqlException or TimeoutException or InvalidOperationException)
        {
            var problem = exception switch
            {
                SqlException { Number: -2 } => $"The query took longer than {ReadOnlySqlExecutor.TimeoutSeconds} seconds. Make it simpler or more selective.",
                SqlException { Number: 229 or 230 or 262 or 297 or 916 } => "Permission denied: only the reporting views can be read.",
                SqlException sqlException => sqlException.Message,
                _ => exception.Message,
            };
            logger.LogInformation("AI query failed: {Problem}", problem);
            await LogAsync(runId, question, sql, runAs, AiQueryOutcome.Failed, problem, 0, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new GuardedQueryOutcome(AiQueryOutcome.Failed, null, problem);
        }
    }

    private async Task LogAsync(Guid runId, string question, string sql, string runAs, AiQueryOutcome outcome, string? error, int rows, int ms)
    {
        db.AiQueryLogs.Add(new AiQueryLog
        {
            RunId = runId,
            UserId = currentUser.UserId,
            Question = question.Length > 1000 ? question[..1000] : question,
            Sql = sql,
            RunAs = runAs,
            Outcome = outcome,
            Error = error is { Length: > 1000 } ? error[..1000] : error,
            RowCount = rows,
            DurationMs = ms,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        });
        // Not the request's token: an attempt that was made is logged even if the user stops waiting.
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
