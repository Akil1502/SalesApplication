using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Small reference lists for dropdowns.</summary>
public sealed class LookupsController(ILookupService lookups) : ApiControllerBase
{
    /// <summary>States and union territories with their GST state codes, sorted by name.</summary>
    [HttpGet("states")]
    [ProducesResponseType<IReadOnlyList<StateDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<StateDto>> States(CancellationToken cancellationToken) =>
        [.. (await lookups.GetStatesAsync(cancellationToken)).Values.OrderBy(s => s.Name, StringComparer.Ordinal)];

    /// <summary>Active sales reps and managers, for assigning customers. Managers and admins only.</summary>
    [HttpGet("sales-reps")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<IReadOnlyList<UserLookupDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<UserLookupDto>> SalesReps(CancellationToken cancellationToken) =>
        lookups.GetSalesRepsAsync(cancellationToken);
}
