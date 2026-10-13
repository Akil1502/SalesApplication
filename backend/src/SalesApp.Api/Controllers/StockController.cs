using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>
/// Inventory: stock levels, the movement ledger, goods receipts and adjustments. Everyone can see stock;
/// only managers and admins change it (sales change it through invoices, from phase P3).
/// </summary>
public sealed class StockController(IInventoryService inventory) : ApiControllerBase
{
    /// <summary>Headline counts for the stock page. Stock value is shown to managers and admins only.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<StockSummaryDto>(StatusCodes.Status200OK)]
    public Task<StockSummaryDto> Summary(CancellationToken cancellationToken) => inventory.GetSummaryAsync(cancellationToken);

    /// <summary>Stock on hand per product, with status against the reorder level.</summary>
    [HttpGet("levels")]
    [ProducesResponseType<PagedResult<StockLevelDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<StockLevelDto>> Levels([FromQuery] StockLevelListRequest request, CancellationToken cancellationToken) =>
        inventory.ListLevelsAsync(request, cancellationToken);

    /// <summary>The stock ledger, newest first. Filter by product, movement type and date range.</summary>
    [HttpGet("movements")]
    [ProducesResponseType<PagedResult<StockMovementDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<StockMovementDto>> Movements([FromQuery] StockMovementListRequest request, CancellationToken cancellationToken) =>
        inventory.ListMovementsAsync(request, cancellationToken);

    [HttpGet("receipts")]
    [ProducesResponseType<PagedResult<StockReceiptListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<StockReceiptListItemDto>> Receipts([FromQuery] PageRequest request, CancellationToken cancellationToken) =>
        inventory.ListReceiptsAsync(request, cancellationToken);

    [HttpGet("receipts/{id:int}")]
    [ProducesResponseType<StockReceiptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<StockReceiptDto> Receipt(int id, CancellationToken cancellationToken) => inventory.GetReceiptAsync(id, cancellationToken);

    /// <summary>Records a goods receipt (GRN): stock in for one or more products.</summary>
    [HttpPost("receipts")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<StockReceiptDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StockReceiptDto>> CreateReceipt(StockReceiptCreateRequest request, CancellationToken cancellationToken)
    {
        var receipt = await inventory.CreateReceiptAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Receipt), new { id = receipt.Id }, receipt);
    }

    /// <summary>Corrects stock: add/remove a quantity, or set it to a counted figure. Notes are required.</summary>
    [HttpPost("adjustments")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<StockMovementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StockMovementDto>> Adjust(StockAdjustmentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await inventory.AdjustAsync(request, cancellationToken));
}
