using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>User management. Admins only.</summary>
[Authorize(Roles = Roles.Admin)]
public sealed class UsersController(IUserAdminService users) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<UserDto>> List([FromQuery] UserListRequest request, CancellationToken cancellationToken) =>
        users.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<UserDto> Get(Guid id, CancellationToken cancellationToken) => users.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> Create(UserCreateRequest request, CancellationToken cancellationToken)
    {
        var user = await users.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<UserDto> Update(Guid id, UserUpdateRequest request, CancellationToken cancellationToken) =>
        users.UpdateAsync(id, request, cancellationToken);

    /// <summary>Sets a new password chosen by the admin and clears any lockout.</summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(id, request, cancellationToken);
        return NoContent();
    }
}
