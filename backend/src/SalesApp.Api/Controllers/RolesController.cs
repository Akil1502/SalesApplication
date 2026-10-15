using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Roles as permission sets, and the list of permissions to choose from.</summary>
[RequirePermission(Permissions.UsersView)]
public sealed class RolesController(IRoleAdminService roles) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken cancellationToken) => roles.ListAsync(cancellationToken);

    /// <summary>Every permission, grouped by menu as the sidebar shows them, and the special rights.</summary>
    [HttpGet("permissions")]
    [ProducesResponseType<PermissionCatalogDto>(StatusCodes.Status200OK)]
    public PermissionCatalogDto Catalog() => roles.Catalog();

    [HttpPost]
    [RequirePermission(Permissions.UsersEdit)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoleDto>> Create(RoleSaveRequest request, CancellationToken cancellationToken)
    {
        var role = await roles.CreateAsync(request, cancellationToken);
        return Created($"/api/roles/{role.Id}", role);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.UsersEdit)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<RoleDto> Update(Guid id, RoleSaveRequest request, CancellationToken cancellationToken) =>
        roles.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.UsersEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await roles.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
