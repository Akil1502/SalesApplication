using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>
/// Customers. Every role can read and edit; credit limit, payment terms and assigned rep are
/// changed only by managers and admins (enforced in <see cref="CustomerService"/>).
/// </summary>
public sealed class CustomersController(ICustomerService customers) : ApiControllerBase
{
    /// <summary>Search customers by name, code, trade name, GSTIN, phone or city.</summary>
    [HttpGet]
    [RequirePermission(Permissions.CustomersView, Permissions.OrdersEdit)]
    [ProducesResponseType<PagedResult<CustomerListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CustomerListItemDto>> List([FromQuery] CustomerListRequest request, CancellationToken cancellationToken) =>
        customers.ListAsync(request, cancellationToken);

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.CustomersView, Permissions.OrdersEdit)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<CustomerDto> Get(int id, CancellationToken cancellationToken) => customers.GetAsync(id, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.CustomersEdit)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerDto>> Create(CustomerSaveRequest request, CancellationToken cancellationToken)
    {
        var customer = await customers.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.CustomersEdit)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CustomerDto> Update(int id, CustomerSaveRequest request, CancellationToken cancellationToken) =>
        customers.UpdateAsync(id, request, cancellationToken);
}
