using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Returns;

// ---- Shared -------------------------------------------------------------------------------

/// <summary>Money paid back: to a customer from their credit, or to us by a supplier.</summary>
public sealed record RefundCreateRequest
{
    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? RefundDate { get; init; }

    [DecimalRange(0.01, 999999999, ErrorMessage = "Amount must be more than zero."), DecimalPlaces(2)]
    public decimal Amount { get; init; }

    public PaymentMode Mode { get; init; }

    [MaxLength(50)]
    public string? Reference { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record RefundDto(
    int Id,
    DateOnly RefundDate,
    decimal Amount,
    PaymentMode Mode,
    string? Reference,
    string? Notes,
    string CreatedByName,
    DateTime CreatedAtUtc);

public sealed record ReturnCancelRequest
{
    [Required(ErrorMessage = "Give a reason for cancelling."), MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>Credit set against another invoice or bill of the same customer or supplier.</summary>
public sealed record CreditApplyRequest
{
    /// <summary>The invoice (credit notes) or supplier bill (supplier returns) to set the credit against.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Choose what to set the credit against.")]
    public int DocumentId { get; init; }

    [DecimalRange(0.01, 999999999, ErrorMessage = "Amount must be more than zero."), DecimalPlaces(2)]
    public decimal Amount { get; init; }

    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? Date { get; init; }

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>A credit set against an invoice or bill: which credit, which document, how much.</summary>
public sealed record CreditUseDto(
    int Id,
    int CreditId,
    string CreditNumber,
    int DocumentId,
    string DocumentNumber,
    DateOnly Date,
    decimal Amount,
    bool Reversed,
    string CreatedByName,
    DateTime CreatedAtUtc);

/// <summary>Credit still available on one credit note or supplier return, for choosing which to use.</summary>
public sealed record AvailableCreditDto(int Id, string Number, DateOnly Date, decimal GrandTotal, decimal Available, string RowVersion);

/// <summary>A credit note or supplier return as listed on its invoice or bill.</summary>
public sealed record ReturnSummaryDto(
    int Id,
    string Number,
    DateOnly Date,
    string Kind,
    ReturnReason Reason,
    ReturnDocumentStatus Status,
    bool WithGst,
    decimal GrandTotal);

// ---- Credit notes -------------------------------------------------------------------------

public sealed record CreditNoteListRequest : PageRequest
{
    public ReturnDocumentStatus? Status { get; init; }
    public CreditNoteKind? Kind { get; init; }
    public int? CustomerId { get; init; }
    public int? InvoiceId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Only credit notes with credit still unused.</summary>
    public bool WithCreditOnly { get; init; }
}

public sealed record CreditNoteListItemDto(
    int Id,
    string Number,
    DateOnly CreditNoteDate,
    CreditNoteKind Kind,
    ReturnReason Reason,
    ReturnDocumentStatus Status,
    bool WithGst,
    int InvoiceId,
    string InvoiceNumber,
    int CustomerId,
    string CustomerName,
    decimal TaxableValue,
    decimal GrandTotal,
    decimal CreditAvailable);

public sealed record CreditNoteSummaryDto(
    decimal CreditedThisMonth,
    int CountThisMonth,
    decimal CustomerCreditAvailable,
    int CustomersWithCredit,
    decimal RefundedThisMonth);

public sealed record CreditNoteLineDto(
    int LineNumber,
    int InvoiceLineId,
    int ProductId,
    string Sku,
    string ProductName,
    string HsnCode,
    ProductUnit Unit,
    decimal? Quantity,
    ReturnDisposition? Disposition,
    decimal TaxableValue,
    decimal GstRate,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal LineTotal);

/// <summary>
/// A credit note as printed. CancelBlockedReason is null when it can still be cancelled (in its own month, with its
/// credit not used on other invoices or refunded).
/// </summary>
public sealed record CreditNoteDto(
    int Id,
    string Number,
    string FinancialYear,
    DateOnly CreditNoteDate,
    ReturnDocumentStatus Status,
    CreditNoteKind Kind,
    ReturnReason Reason,
    string? ReasonNote,
    bool WithGst,
    int InvoiceId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string SellerName,
    string SellerGstin,
    string SellerStateCode,
    string SellerAddress,
    int CustomerId,
    string CustomerName,
    string? CustomerGstin,
    InvoiceAddressDto BillingAddress,
    string PlaceOfSupplyStateCode,
    string PlaceOfSupplyStateName,
    bool IsInterState,
    bool UsesUtgst,
    IReadOnlyList<CreditNoteLineDto> Lines,
    IReadOnlyList<HsnSummaryDto> HsnSummary,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal AmountAdjusted,
    decimal AmountRefunded,
    decimal CreditAvailable,
    string AmountInWords,
    IReadOnlyList<CreditUseDto> Adjustments,
    IReadOnlyList<RefundDto> Refunds,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    string? CancelBlockedReason,
    string CreatedByName,
    DateTime CreatedAtUtc,
    string RowVersion);

/// <summary>
/// One invoice line to credit. A goods return gives Quantity and Disposition; a price correction gives Amount, the
/// value (before GST) to take off the line.
/// </summary>
public sealed record CreditNoteLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an invoice line.")]
    public int InvoiceLineId { get; init; }

    [DecimalRange(0.001, 99999999, ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal? Quantity { get; init; }

    public ReturnDisposition? Disposition { get; init; }

    [DecimalRange(0.01, 999999999, ErrorMessage = "Amount must be more than zero."), DecimalPlaces(2)]
    public decimal? Amount { get; init; }
}

public sealed record CreditNoteCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the invoice.")]
    public int InvoiceId { get; init; }

    /// <summary>Defaults to today (India time). Can't be in the future, before the invoice, or before the latest credit note this year.</summary>
    public DateOnly? CreditNoteDate { get; init; }

    public CreditNoteKind Kind { get; init; }

    public ReturnReason Reason { get; init; }

    [MaxLength(500)]
    public string? ReasonNote { get; init; }

    [Required, MinLength(1, ErrorMessage = "Choose at least one line."), MaxLength(200)]
    public List<CreditNoteLineInput> Lines { get; init; } = [];
}

/// <summary>What a credit note would come to, worked out exactly as saving it would, without saving anything.</summary>
public sealed record CreditNotePreviewDto(
    IReadOnlyList<CreditNotePreviewLineDto> Lines,
    bool WithGst,
    DateOnly GstDeadline,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal OffInvoice,
    decimal LeftAsCredit);

public sealed record CreditNotePreviewLineDto(int InvoiceLineId, decimal TaxableValue, decimal TaxAmount, decimal LineTotal);

// ---- Supplier returns ---------------------------------------------------------------------

public sealed record SupplierReturnListRequest : PageRequest
{
    public ReturnDocumentStatus? Status { get; init; }
    public int? SupplierId { get; init; }
    public int? SupplierBillId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Only returns with credit still unused.</summary>
    public bool WithCreditOnly { get; init; }

    /// <summary>Only returns whose supplier credit note hasn't been noted yet.</summary>
    public bool AwaitingSupplierNoteOnly { get; init; }
}

public sealed record SupplierReturnListItemDto(
    int Id,
    string Number,
    DateOnly ReturnDate,
    ReturnReason Reason,
    ReturnDocumentStatus Status,
    bool WithGst,
    int SupplierBillId,
    string BillNumber,
    string SupplierInvoiceNumber,
    int SupplierId,
    string SupplierName,
    string? SupplierCreditNoteNumber,
    decimal TaxableValue,
    decimal GrandTotal,
    decimal CreditAvailable);

public sealed record SupplierReturnLineDto(
    int LineNumber,
    int SupplierBillLineId,
    int ProductId,
    string Sku,
    string ProductName,
    string HsnCode,
    ProductUnit Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxableValue,
    decimal GstRate,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal LineTotal);

/// <summary>A debit note as printed. Our company's details are today's, like the bank details on an invoice.</summary>
public sealed record SupplierReturnDto(
    int Id,
    string Number,
    string FinancialYear,
    DateOnly ReturnDate,
    ReturnDocumentStatus Status,
    ReturnReason Reason,
    string? ReasonNote,
    bool WithGst,
    int SupplierBillId,
    string BillNumber,
    string SupplierInvoiceNumber,
    DateOnly BillDate,
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    int SupplierId,
    string SupplierName,
    string? SupplierGstin,
    string SupplierStateCode,
    string SupplierStateName,
    bool IsInterState,
    string CompanyName,
    string CompanyGstin,
    string CompanyAddress,
    string? SupplierCreditNoteNumber,
    DateOnly? SupplierCreditNoteDate,
    IReadOnlyList<SupplierReturnLineDto> Lines,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal AmountAdjusted,
    decimal AmountRefunded,
    decimal CreditAvailable,
    string AmountInWords,
    IReadOnlyList<CreditUseDto> Adjustments,
    IReadOnlyList<RefundDto> Refunds,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    string? CancelBlockedReason,
    string CreatedByName,
    DateTime CreatedAtUtc,
    string RowVersion);

public sealed record SupplierReturnLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a bill line.")]
    public int SupplierBillLineId { get; init; }

    [DecimalRange(0.001, 99999999, ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }
}

public sealed record SupplierReturnCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the supplier bill.")]
    public int SupplierBillId { get; init; }

    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? ReturnDate { get; init; }

    public ReturnReason Reason { get; init; }

    [MaxLength(500)]
    public string? ReasonNote { get; init; }

    [MaxLength(30)]
    public string? SupplierCreditNoteNumber { get; init; }

    public DateOnly? SupplierCreditNoteDate { get; init; }

    [Required, MinLength(1, ErrorMessage = "Choose at least one line."), MaxLength(200)]
    public List<SupplierReturnLineInput> Lines { get; init; } = [];
}

/// <summary>Notes the supplier's credit note for the returned goods once it arrives.</summary>
public sealed record SupplierReturnNoteRequest
{
    [Required(ErrorMessage = "Enter the supplier's credit note number."), MaxLength(30)]
    public string SupplierCreditNoteNumber { get; init; } = string.Empty;

    public DateOnly? SupplierCreditNoteDate { get; init; }

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record SupplierReturnPreviewDto(
    IReadOnlyList<SupplierReturnPreviewLineDto> Lines,
    bool WithGst,
    DateOnly GstDeadline,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal OffBill,
    decimal LeftAsCredit);

public sealed record SupplierReturnPreviewLineDto(int SupplierBillLineId, decimal TaxableValue, decimal TaxAmount, decimal LineTotal);

public sealed record SupplierReturnSummaryDto(
    decimal ReturnedThisMonth,
    int CountThisMonth,
    decimal SupplierCreditAvailable,
    int AwaitingSupplierNote);
