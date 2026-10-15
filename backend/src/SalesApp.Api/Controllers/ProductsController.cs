using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Products. Everyone can browse; only managers and admins maintain the catalogue (and see cost prices).</summary>
public sealed class ProductsController(IProductService products) : ApiControllerBase
{
    /// <summary>Search products by SKU, name or HSN code.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ProductsView, Permissions.OrdersEdit, Permissions.StockEdit)]
    [ProducesResponseType<PagedResult<ProductListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ProductListItemDto>> List([FromQuery] ProductListRequest request, CancellationToken cancellationToken) =>
        products.ListAsync(request, cancellationToken);

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.ProductsView, Permissions.OrdersEdit, Permissions.StockEdit)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProductDto> Get(int id, CancellationToken cancellationToken) => products.GetAsync(id, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.ProductsEdit)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductDto>> Create(ProductSaveRequest request, CancellationToken cancellationToken)
    {
        var product = await products.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.ProductsEdit)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ProductDto> Update(int id, ProductSaveRequest request, CancellationToken cancellationToken) =>
        products.UpdateAsync(id, request, cancellationToken);
}
