using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesApp.AI.Insights;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Insights;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.Api.Controllers;

/// <summary>
/// Insights for managers: the forecast, trends, reorder suggestions and anomalies (computed without AI), with a
/// written summary whose figures have been checked against them. Recomputed nightly and on request.
/// </summary>
[Route("api/insights")]
[Authorize(Roles = Roles.AdminOrManager)]
public sealed class InsightsController(IInsightsService insights) : ApiControllerBase
{
    /// <summary>The latest insights, or 204 No Content if they've never been computed.</summary>
    [HttpGet]
    [ProducesResponseType<InsightsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Latest(CancellationToken cancellationToken) =>
        await insights.GetLatestAsync(cancellationToken) is { } latest ? Ok(latest) : NoContent();

    /// <summary>Recomputes the insights now. Takes a few seconds for the figures, plus the written summary when AI is on.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting(AiRateLimit.PolicyName)]
    [ProducesResponseType<InsightsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status429TooManyRequests)]
    public Task<InsightsDto> Refresh(CancellationToken cancellationToken) => insights.RunAsync(InsightTrigger.Manual, cancellationToken);
}
