using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.AI.Claude;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Security;

namespace SalesApp.Api.Controllers;

/// <summary>
/// AI spend and activity for administrators: what each feature and user costs, every call to Claude, and every SQL
/// query the AI wrote (with what the checker or the database said about it). Conversations themselves stay private.
/// </summary>
[Route("api/admin/ai-usage")]
[Authorize(Roles = Roles.Admin)]
public sealed class AiUsageController(IAiUsageReport report) : ApiControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<AiUsageSummaryDto>(StatusCodes.Status200OK)]
    public Task<AiUsageSummaryDto> Summary([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        report.SummaryAsync(days, cancellationToken);

    [HttpGet("calls")]
    [ProducesResponseType<PagedResult<AiCallDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AiCallDto>> Calls([FromQuery] AiCallListRequest request, CancellationToken cancellationToken) =>
        report.CallsAsync(request, cancellationToken);

    [HttpGet("queries")]
    [ProducesResponseType<PagedResult<AiQueryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AiQueryDto>> Queries([FromQuery] AiQueryListRequest request, CancellationToken cancellationToken) =>
        report.QueriesAsync(request, cancellationToken);
}
