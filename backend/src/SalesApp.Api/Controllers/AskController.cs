using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SalesApp.AI;
using SalesApp.AI.AskData;
using SalesApp.AI.Claude;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Reporting;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>
/// "Ask your data": plain-English questions answered from the reporting views. The answer streams as server-sent
/// events (progress steps, then the answer) so the user sees what's happening during the 10-30 seconds it takes.
/// </summary>
[Route("api/ask")]
[RequirePermission(Permissions.AskDataUse)]
public sealed class AskController(
    IAskDataService askData,
    IClaudeGateway claude,
    ICompanySettingsService settings,
    IAuditLog audit,
    IOptions<JsonOptions> json,
    ILogger<AskController> logger) : ApiControllerBase
{
    /// <summary>
    /// Streams <c>text/event-stream</c>: <c>step</c> events (AskStepDto) while working, then one <c>answer</c>
    /// event (AskAnswerDto), or an <c>error</c> event with a message if it can't be answered.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimits.Ai)]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task Ask(AskRequest request, CancellationToken cancellationToken)
    {
        // Checked before the stream starts, so the browser gets an ordinary error response.
        if (!claude.IsAvailable)
        {
            throw new AiUnavailableException("Ask your data needs the AI features, which are switched off or have no API key.");
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var options = json.Value.JsonSerializerOptions;
        async Task Send(string name, object data)
        {
            await Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, options)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        try
        {
            var answer = await askData.AskAsync(request.Question, step => Send("step", step), cancellationToken);
            await Send("answer", answer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user pressed Stop or closed the page; nobody is listening.
        }
        catch (Exception exception) when (exception is AiUnavailableException or AiBudgetExceededException or AiRefusedException
            or AiServiceException or ValidationException)
        {
            await Send("error", new { message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ask-your-data failed");
            await Send("error", new { message = "Something went wrong while answering. Please try again." });
        }
    }

    /// <summary>The rows of an answer as an Excel file. Results are kept for an hour after the question.</summary>
    [HttpGet("results/{id:guid}/excel")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excel(Guid id, CancellationToken cancellationToken)
    {
        var stored = askData.GetResult(id) ?? throw new NotFoundException("That answer has expired. Ask the question again to download it.");
        var columns = stored.Result.Columns
            .Select((c, i) => new ReportColumn($"c{i}", ColumnLabel.From(c.Name), c.Kind switch
            {
                QueryColumnKind.Money => ReportColumnType.Money,
                QueryColumnKind.Count => ReportColumnType.Count,
                QueryColumnKind.Number => ReportColumnType.Quantity,
                QueryColumnKind.Date => ReportColumnType.Date,
                _ => ReportColumnType.Text,
            }))
            .ToList();
        var rows = stored.Result.Rows
            .Select(row => columns.Select((c, i) => (c.Key, Value: ExcelValue(row[i], c.Type))).ToDictionary(x => x.Key, x => x.Value))
            .ToList();

        var title = stored.Question.Length > 80 ? stored.Question[..80] + "…" : stored.Question;
        var report = new ReportResult("ask", title, $"Answer from Ask your data. SQL: {stored.Sql.ReplaceLineEndings(" ")}", null, null,
            columns, rows, new Dictionary<string, object?>());
        var company = await settings.GetAsync(cancellationToken);
        var bytes = ExcelReportWriter.Write(report, company.TradeName ?? company.LegalName);
        await audit.RecordAsync(new AuditEvent(AuditAction.Exported, Summary: $"Downloaded an Ask your data answer as Excel: \"{title}\" ({rows.Count} rows)."), cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"answer_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    private static object? ExcelValue(object? value, ReportColumnType type) => value switch
    {
        string text when type == ReportColumnType.Date && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date,
        int number => number,
        long number => (decimal)number,
        short number => (int)number,
        byte number => (int)number,
        double number => (decimal)number,
        float number => (decimal)number,
        bool flag => flag ? "Yes" : "No",
        _ => value,
    };
}
