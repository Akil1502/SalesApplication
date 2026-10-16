using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Returns;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Credit notes against invoices, and the credit customers have with us.</summary>
[Route("api/credit-notes")]
[RequirePermission(Permissions.CreditNotesView)]
public sealed class CreditNotesController(ICreditNoteService creditNotes) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<CreditNoteListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CreditNoteListItemDto>> List([FromQuery] CreditNoteListRequest request, CancellationToken cancellationToken) =>
        creditNotes.ListAsync(request, cancellationToken);

    /// <summary>Credited this month, customer credit still unused, refunds this month.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<CreditNoteSummaryDto>(StatusCodes.Status200OK)]
    public Task<CreditNoteSummaryDto> Summary(CancellationToken cancellationToken) => creditNotes.GetSummaryAsync(cancellationToken);

    /// <summary>A customer's credit notes with credit left, oldest first, for setting against an invoice.</summary>
    [HttpGet("available")]
    [ProducesResponseType<IReadOnlyList<AvailableCreditDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AvailableCreditDto>> Available([FromQuery] int customerId, CancellationToken cancellationToken) =>
        creditNotes.AvailableAsync(customerId, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<CreditNoteDto> Get(int id, CancellationToken cancellationToken) => creditNotes.GetAsync(id, cancellationToken);

    /// <summary>Works a credit note out exactly as issuing it would, without saving anything.</summary>
    [HttpPost("preview")]
    [RequirePermission(Permissions.CreditNotesEdit)]
    [ProducesResponseType<CreditNotePreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CreditNotePreviewDto> Preview(CreditNoteCreateRequest request, CancellationToken cancellationToken) =>
        creditNotes.PreviewAsync(request, cancellationToken);

    /// <summary>Issues a credit note against an invoice: returned goods go back into stock (or are written off) and the value comes off what's due.</summary>
    [HttpPost]
    [RequirePermission(Permissions.CreditNotesEdit)]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreditNoteDto>> Create(CreditNoteCreateRequest request, CancellationToken cancellationToken)
    {
        var note = await creditNotes.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = note.Id }, note);
    }

    /// <summary>Sets credit from this note against another of the customer's invoices.</summary>
    [HttpPost("{id:int}/adjustments")]
    [RequirePermission(Permissions.CreditNotesEdit)]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CreditNoteDto> Apply(int id, CreditApplyRequest request, CancellationToken cancellationToken) =>
        creditNotes.ApplyAsync(id, request, cancellationToken);

    /// <summary>Records credit paid back to the customer.</summary>
    [HttpPost("{id:int}/refunds")]
    [RequirePermission(Permissions.CreditNotesEdit)]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CreditNoteDto> Refund(int id, RefundCreateRequest request, CancellationToken cancellationToken) =>
        creditNotes.RefundAsync(id, request, cancellationToken);

    /// <summary>Cancels a credit note in its own month whose credit hasn't been used elsewhere or refunded.</summary>
    [HttpPost("{id:int}/cancel")]
    [RequirePermission(Permissions.CreditNotesEdit)]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CreditNoteDto> Cancel(int id, ReturnCancelRequest request, CancellationToken cancellationToken) =>
        creditNotes.CancelAsync(id, request, cancellationToken);
}

/// <summary>Goods returned to suppliers (our debit notes), and the credit we have with them.</summary>
[Route("api/supplier-returns")]
[RequirePermission(Permissions.SupplierReturnsView)]
public sealed class SupplierReturnsController(ISupplierReturnService returns) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupplierReturnListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SupplierReturnListItemDto>> List([FromQuery] SupplierReturnListRequest request, CancellationToken cancellationToken) =>
        returns.ListAsync(request, cancellationToken);

    /// <summary>Returned this month, supplier credit still unused, returns waiting for the supplier's credit note.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<SupplierReturnSummaryDto>(StatusCodes.Status200OK)]
    public Task<SupplierReturnSummaryDto> Summary(CancellationToken cancellationToken) => returns.GetSummaryAsync(cancellationToken);

    /// <summary>A supplier's returns with credit left, oldest first, for setting against a bill.</summary>
    [HttpGet("available")]
    [ProducesResponseType<IReadOnlyList<AvailableCreditDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AvailableCreditDto>> Available([FromQuery] int supplierId, CancellationToken cancellationToken) =>
        returns.AvailableAsync(supplierId, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<SupplierReturnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SupplierReturnDto> Get(int id, CancellationToken cancellationToken) => returns.GetAsync(id, cancellationToken);

    [HttpPost("preview")]
    [RequirePermission(Permissions.SupplierReturnsEdit)]
    [ProducesResponseType<SupplierReturnPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierReturnPreviewDto> Preview(SupplierReturnCreateRequest request, CancellationToken cancellationToken) =>
        returns.PreviewAsync(request, cancellationToken);

    /// <summary>Returns goods against a supplier bill: stock out at the bill's price, input GST reversed, what we owe reduced.</summary>
    [HttpPost]
    [RequirePermission(Permissions.SupplierReturnsEdit)]
    [ProducesResponseType<SupplierReturnDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierReturnDto>> Create(SupplierReturnCreateRequest request, CancellationToken cancellationToken)
    {
        var ret = await returns.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = ret.Id }, ret);
    }

    /// <summary>Notes the supplier's credit note for the returned goods.</summary>
    [HttpPut("{id:int}/supplier-credit-note")]
    [RequirePermission(Permissions.SupplierReturnsEdit)]
    [ProducesResponseType<SupplierReturnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierReturnDto> NoteSupplierCreditNote(int id, SupplierReturnNoteRequest request, CancellationToken cancellationToken) =>
        returns.NoteSupplierCreditNoteAsync(id, request, cancellationToken);

    /// <summary>Sets credit from this return against another of the supplier's bills.</summary>
    [HttpPost("{id:int}/adjustments")]
    [RequirePermission(Permissions.SupplierReturnsEdit)]
    [ProducesResponseType<SupplierReturnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierReturnDto> Apply(int id, CreditApplyRequest request, CancellationToken cancellationToken) =>
        returns.ApplyAsync(id, request, cancellationToken);

    /// <summary>Records money the supplier paid back for the returned goods.</summary>
    [HttpPost("{id:int}/refunds")]
    [RequirePermission(Permissions.SupplierReturnsEdit)]
    [ProducesResponseType<SupplierReturnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierReturnDto> Refund(int id, RefundCreateRequest request, CancellationToken cancellationToken) =>
        returns.RefundAsync(id, request, cancellationToken);

    /// <summary>Cancels a return in its own month whose credit hasn't been used elsewhere or refunded; the goods come back into stock.</summary>
    [HttpPost("{id:int}/cancel")]
    [RequirePermission(Permissions.SupplierReturnsEdit)]
    [ProducesResponseType<SupplierReturnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SupplierReturnDto> Cancel(int id, ReturnCancelRequest request, CancellationToken cancellationToken) =>
        returns.CancelAsync(id, request, cancellationToken);
}
