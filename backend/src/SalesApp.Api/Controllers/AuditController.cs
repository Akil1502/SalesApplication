using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;

namespace SalesApp.Api.Controllers;

/// <summary>The audit trail: every change with old and new values, sign-ins and downloads. Administrators by default.</summary>
[RequirePermission(Permissions.AuditView)]
public sealed class AuditController(IAuditLog audit) : ApiControllerBase
{
    /// <summary>Entries newest first. Filter by record (EntityType and EntityId) for one record's history.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AuditEntryDto>> List([FromQuery] AuditListRequest request, CancellationToken cancellationToken) =>
        audit.ListAsync(request, cancellationToken);
}
