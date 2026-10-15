using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Product categories: readable by everyone, maintained by managers and admins.</summary>
public sealed class CategoriesController(ICategoryService categories) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CategoryDto>> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        categories.ListAsync(includeInactive, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.CatalogEdit)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CategoryDto>> Create(CategorySaveRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await categories.CreateAsync(request, cancellationToken));

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.CatalogEdit)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<CategoryDto> Update(int id, CategorySaveRequest request, CancellationToken cancellationToken) =>
        categories.UpdateAsync(id, request, cancellationToken);

    /// <summary>Deletes an unused category. Categories with products return 409; deactivate those instead.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.CatalogEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await categories.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>HSN codes and GST rates: readable by everyone, maintained by managers and admins.</summary>
[Route("api/hsn-codes")]
public sealed class HsnCodesController(IHsnCodeService hsnCodes) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HsnCodeDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<HsnCodeDto>> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        hsnCodes.ListAsync(includeInactive, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.CatalogEdit)]
    [ProducesResponseType<HsnCodeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HsnCodeDto>> Create(HsnCodeSaveRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await hsnCodes.CreateAsync(request, cancellationToken));

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.CatalogEdit)]
    [ProducesResponseType<HsnCodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<HsnCodeDto> Update(int id, HsnCodeSaveRequest request, CancellationToken cancellationToken) =>
        hsnCodes.UpdateAsync(id, request, cancellationToken);

    /// <summary>Deletes an unused HSN code. Codes used by products return 409; deactivate those instead.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.CatalogEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await hsnCodes.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
