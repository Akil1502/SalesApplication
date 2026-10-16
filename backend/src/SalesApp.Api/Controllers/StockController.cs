using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>
/// Inventory: stock levels, the movement ledger, goods receipts and adjustments. Goods are received against purchase
/// orders (POST /api/purchase-orders/{id}/receipts); sales take stock out through invoices.
/// </summary>
[RequirePermission(Permissions.StockView)]
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

    /// <summary>Corrects stock: add/remove a quantity, or set it to a counted figure. Notes are required.</summary>
    [HttpPost("adjustments")]
    [RequirePermission(Permissions.StockEdit)]
    [ProducesResponseType<StockMovementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StockMovementDto>> Adjust(StockAdjustmentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await inventory.AdjustAsync(request, cancellationToken));
}
