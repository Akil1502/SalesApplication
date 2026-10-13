using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Quotations and sales orders. Every role can create, quote, confirm and cancel them.</summary>
public sealed class OrdersController(IOrderService orders) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<OrderListItemDto>> List([FromQuery] OrderListRequest request, CancellationToken cancellationToken) =>
        orders.ListAsync(request, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OrderDto> Get(int id, CancellationToken cancellationToken) => orders.GetAsync(id, cancellationToken);

    /// <summary>Creates a draft order. Prices default to the products' list prices.</summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderDto>> Create(OrderSaveRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Edits a draft or quoted order.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<OrderDto> Update(int id, OrderSaveRequest request, CancellationToken cancellationToken) =>
        orders.UpdateAsync(id, request, cancellationToken);

    /// <summary>Draft → Quoted. Assigns a quotation number.</summary>
    [HttpPost("{id:int}/quote")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<OrderDto> Quote(int id, OrderActionRequest request, CancellationToken cancellationToken) =>
        orders.QuoteAsync(id, request, cancellationToken);

    /// <summary>Draft/Quoted → Confirmed, after checking the customer's credit limit.</summary>
    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<OrderDto> Confirm(int id, OrderConfirmRequest request, CancellationToken cancellationToken) =>
        orders.ConfirmAsync(id, request, cancellationToken);

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<OrderDto> Cancel(int id, OrderCancelRequest request, CancellationToken cancellationToken) =>
        orders.CancelAsync(id, request, cancellationToken);
}

/// <summary>Tax invoices. Everyone can view them; managers and admins issue and cancel them.</summary>
public sealed class InvoicesController(IInvoiceService invoices) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<InvoiceListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<InvoiceListItemDto>> List([FromQuery] InvoiceListRequest request, CancellationToken cancellationToken) =>
        invoices.ListAsync(request, cancellationToken);

    /// <summary>Receivables at a glance: outstanding, overdue, invoiced and collected this month.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<InvoiceSummaryDto>(StatusCodes.Status200OK)]
    public Task<InvoiceSummaryDto> Summary(CancellationToken cancellationToken) => invoices.GetSummaryAsync(cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<InvoiceDto> Get(int id, CancellationToken cancellationToken) => invoices.GetAsync(id, cancellationToken);

    /// <summary>Issues an invoice for a confirmed order and takes its goods out of stock.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceCreateRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoices.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
    }

    /// <summary>Cancels an unpaid invoice: it stays in the register, its goods return to stock and the order reopens.</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<InvoiceDto> Cancel(int id, InvoiceCancelRequest request, CancellationToken cancellationToken) =>
        invoices.CancelAsync(id, request, cancellationToken);
}

/// <summary>Customer payments. Everyone can view them; managers and admins record them.</summary>
public sealed class PaymentsController(IPaymentService payments) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<PaymentDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<PaymentDto>> List([FromQuery] PaymentListRequest request, CancellationToken cancellationToken) =>
        payments.ListAsync(request, cancellationToken);

    [HttpPost]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Create(PaymentCreateRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await payments.CreateAsync(request, cancellationToken));
}
