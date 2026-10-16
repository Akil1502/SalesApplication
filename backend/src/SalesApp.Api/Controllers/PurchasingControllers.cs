using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Suppliers, with what we owe them.</summary>
[RequirePermission(Permissions.SuppliersView)]
public sealed class SuppliersController(ISupplierService suppliers) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupplierListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SupplierListItemDto>> List([FromQuery] SupplierListRequest request, CancellationToken cancellationToken) =>
        suppliers.ListAsync(request, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SupplierDto> Get(int id, CancellationToken cancellationToken) => suppliers.GetAsync(id, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.SuppliersEdit)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupplierDto>> Create(SupplierSaveRequest request, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = supplier.Id }, supplier);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.SuppliersEdit)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierDto> Update(int id, SupplierSaveRequest request, CancellationToken cancellationToken) =>
        suppliers.UpdateAsync(id, request, cancellationToken);
}

/// <summary>
/// Purchase orders: drafted, placed (after approval when above the company's limit), received against and closed.
/// Receiving needs the stock right; everything that changes an order needs the purchase orders right.
/// </summary>
[Route("api/purchase-orders")]
[RequirePermission(Permissions.PurchaseOrdersView)]
public sealed class PurchaseOrdersController(IPurchaseOrderService orders) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<PurchaseOrderListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<PurchaseOrderListItemDto>> List([FromQuery] PurchaseOrderListRequest request, CancellationToken cancellationToken) =>
        orders.ListAsync(request, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<PurchaseOrderDto> Get(int id, CancellationToken cancellationToken) => orders.GetAsync(id, cancellationToken);

    /// <summary>Creates a draft order. Prices default to the last price paid to the supplier.</summary>
    [HttpPost]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PurchaseOrderDto>> Create(PurchaseOrderSaveRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Edits a draft order.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<PurchaseOrderDto> Update(int id, PurchaseOrderSaveRequest request, CancellationToken cancellationToken) =>
        orders.UpdateAsync(id, request, cancellationToken);

    /// <summary>Draft → Ordered, or → PendingApproval when above the approval limit and the user can't approve.</summary>
    [HttpPost("{id:int}/place")]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<PurchaseOrderDto> Place(int id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        orders.PlaceAsync(id, request, cancellationToken);

    [HttpPost("{id:int}/approve")]
    [RequirePermission(Permissions.PurchaseOrdersApprove)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<PurchaseOrderDto> Approve(int id, PurchaseOrderActionRequest request, CancellationToken cancellationToken) =>
        orders.ApproveAsync(id, request, cancellationToken);

    /// <summary>Sends an order waiting for approval back to draft, with the reason.</summary>
    [HttpPost("{id:int}/reject")]
    [RequirePermission(Permissions.PurchaseOrdersApprove)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<PurchaseOrderDto> Reject(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken) =>
        orders.RejectAsync(id, request, cancellationToken);

    [HttpPost("{id:int}/cancel")]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<PurchaseOrderDto> Cancel(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken) =>
        orders.CancelAsync(id, request, cancellationToken);

    /// <summary>Closes a part-received order: nothing more will come.</summary>
    [HttpPost("{id:int}/close")]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<PurchaseOrderDto> Close(int id, PurchaseOrderReasonRequest request, CancellationToken cancellationToken) =>
        orders.CloseAsync(id, request, cancellationToken);

    /// <summary>Records goods received against the order (a GRN): stock in at the order's price.</summary>
    [HttpPost("{id:int}/receipts")]
    [RequirePermission(Permissions.StockEdit)]
    [ProducesResponseType<StockReceiptDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockReceiptDto>> Receive(int id, GoodsReceiptCreateRequest request, CancellationToken cancellationToken)
    {
        var receipt = await orders.ReceiveAsync(id, request, cancellationToken);
        return Created($"/api/stock/receipts/{receipt.Id}", receipt);
    }

    /// <summary>Products at or below their reorder level that open orders don't cover, with what to order.</summary>
    [HttpGet("reorder-candidates")]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<IReadOnlyList<ReorderCandidateDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ReorderCandidateDto>> ReorderCandidates(CancellationToken cancellationToken) =>
        orders.ReorderCandidatesAsync(cancellationToken);

    /// <summary>Turns chosen products into draft orders, one per preferred supplier.</summary>
    [HttpPost("reorder-drafts")]
    [RequirePermission(Permissions.PurchaseOrdersEdit)]
    [ProducesResponseType<IReadOnlyList<PurchaseOrderListItemDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PurchaseOrderListItemDto>>> ReorderDrafts(ReorderDraftRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await orders.CreateReorderDraftsAsync(request, cancellationToken));
}

/// <summary>Supplier bills, checked against their order and what was received.</summary>
[Route("api/supplier-bills")]
[RequirePermission(Permissions.SupplierBillsView)]
public sealed class SupplierBillsController(ISupplierBillService bills) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupplierBillListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SupplierBillListItemDto>> List([FromQuery] SupplierBillListRequest request, CancellationToken cancellationToken) =>
        bills.ListAsync(request, cancellationToken);

    /// <summary>Payables at a glance: owed, overdue, due in the next week, waiting for approval, paid this month.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<SupplierBillSummaryDto>(StatusCodes.Status200OK)]
    public Task<SupplierBillSummaryDto> Summary(CancellationToken cancellationToken) => bills.GetSummaryAsync(cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<SupplierBillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SupplierBillDto> Get(int id, CancellationToken cancellationToken) => bills.GetAsync(id, cancellationToken);

    /// <summary>Enters a supplier's bill against a purchase order and runs the three-way check.</summary>
    [HttpPost]
    [RequirePermission(Permissions.SupplierBillsEdit)]
    [ProducesResponseType<SupplierBillDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierBillDto>> Create(SupplierBillCreateRequest request, CancellationToken cancellationToken)
    {
        var bill = await bills.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = bill.Id }, bill);
    }

    /// <summary>Accepts a bill's differences from its order, so it can be paid.</summary>
    [HttpPost("{id:int}/approve")]
    [RequirePermission(Permissions.SupplierBillsApprove)]
    [ProducesResponseType<SupplierBillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierBillDto> Approve(int id, SupplierBillApproveRequest request, CancellationToken cancellationToken) =>
        bills.ApproveAsync(id, request, cancellationToken);

    /// <summary>Cancels an unpaid bill: it stays on record and its quantities count as unbilled again.</summary>
    [HttpPost("{id:int}/cancel")]
    [RequirePermission(Permissions.SupplierBillsEdit)]
    [ProducesResponseType<SupplierBillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierBillDto> Cancel(int id, SupplierBillCancelRequest request, CancellationToken cancellationToken) =>
        bills.CancelAsync(id, request, cancellationToken);
}

/// <summary>Payments made to suppliers.</summary>
[Route("api/supplier-payments")]
[RequirePermission(Permissions.SupplierPaymentsView)]
public sealed class SupplierPaymentsController(ISupplierPaymentService payments) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupplierPaymentDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SupplierPaymentDto>> List([FromQuery] SupplierPaymentListRequest request, CancellationToken cancellationToken) =>
        payments.ListAsync(request, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.SupplierPaymentsEdit)]
    [ProducesResponseType<SupplierPaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierPaymentDto>> Create(SupplierPaymentCreateRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await payments.CreateAsync(request, cancellationToken));
}
