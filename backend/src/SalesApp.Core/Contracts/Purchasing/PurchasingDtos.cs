using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Purchasing;

// ---- Suppliers ------------------------------------------------------------------------

public sealed record SupplierListRequest : PageRequest
{
    [StringLength(2, MinimumLength = 2)]
    public string? StateCode { get; init; }
}

/// <summary>A supplier in a list, with what we owe them and how many orders are still open.</summary>
public sealed record SupplierListItemDto(
    int Id,
    string Code,
    string Name,
    string? Gstin,
    string City,
    string StateName,
    string? Phone,
    decimal Payable,
    decimal Overdue,
    int OpenOrders,
    bool IsActive);

/// <summary>A supplier in full. IsInterState: their bills carry IGST (they're in another state from the company).</summary>
public sealed record SupplierDto(
    int Id,
    string Code,
    string Name,
    string? TradeName,
    string? Gstin,
    string? Pan,
    string? ContactPerson,
    string? Phone,
    string? Email,
    AddressDto Address,
    int PaymentTermsDays,
    string? BankName,
    string? BankAccountNumber,
    string? BankIfsc,
    string? Notes,
    bool IsActive,
    bool IsInterState,
    decimal Payable,
    decimal Overdue,
    int OpenOrders,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion);

public sealed record SupplierSaveRequest
{
    /// <summary>Optional on create (generated when blank); ignored on update because codes are permanent.</summary>
    [MaxLength(20), RegularExpression("^[A-Za-z0-9-]*$", ErrorMessage = "Code may contain letters, digits and '-'.")]
    public string? Code { get; init; }

    [Required, MaxLength(150)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(150)]
    public string? TradeName { get; init; }

    [Gstin]
    public string? Gstin { get; init; }

    [MaxLength(100)]
    public string? ContactPerson { get; init; }

    [IndianPhone, MaxLength(20)]
    public string? Phone { get; init; }

    [EmailAddress, MaxLength(256)]
    public string? Email { get; init; }

    [Required]
    public AddressInput Address { get; init; } = new();

    [Range(0, 365, ErrorMessage = "Payment terms must be between 0 and 365 days.")]
    public int PaymentTermsDays { get; init; } = 30;

    [MaxLength(100)]
    public string? BankName { get; init; }

    [RegularExpression("^[0-9]{9,18}$", ErrorMessage = "Account number must be 9 to 18 digits.")]
    public string? BankAccountNumber { get; init; }

    [Ifsc]
    public string? BankIfsc { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Required when updating; the value from the last GET, for optimistic concurrency.</summary>
    public string? RowVersion { get; init; }
}

// ---- Purchase orders ------------------------------------------------------------------

public sealed record PurchaseOrderListRequest : PageRequest
{
    public PurchaseOrderStatus? Status { get; init; }
    public int? SupplierId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Only orders with goods still to come (Ordered or PartlyReceived).</summary>
    public bool OpenOnly { get; init; }

    /// <summary>Only open orders past their expected date.</summary>
    public bool LateOnly { get; init; }
}

public sealed record PurchaseOrderListItemDto(
    int Id,
    string Number,
    PurchaseOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    int SupplierId,
    string SupplierName,
    int LineCount,
    decimal GrandTotal,
    decimal ReceivedPercent,
    bool IsLate,
    string CreatedByName);

/// <summary>
/// An order line with its progress. QuantityPending is what's still to come; MaxReceivable is how much more can be
/// received, allowing the company's over-receipt tolerance.
/// </summary>
public sealed record PurchaseOrderLineDto(
    int Id,
    int LineNumber,
    int ProductId,
    string Sku,
    string ProductName,
    string HsnCode,
    ProductUnit Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal GstRate,
    decimal TaxableValue,
    decimal TaxAmount,
    decimal LineTotal,
    decimal QuantityReceived,
    decimal QuantityBilled,
    decimal QuantityPending,
    decimal MaxReceivable,
    decimal QuantityOnHand);

/// <summary>A delivery against the order.</summary>
public sealed record PurchaseReceiptSummaryDto(int Id, string Number, DateOnly ReceiptDate, string? SupplierReference, int LineCount, string CreatedByName);

/// <summary>A bill entered against the order.</summary>
public sealed record PurchaseBillSummaryDto(
    int Id,
    string Number,
    string SupplierInvoiceNumber,
    DateOnly BillDate,
    decimal GrandTotal,
    SupplierBillStatus Status,
    BillMatchStatus MatchStatus);

/// <summary>
/// A purchase order as printed, with its progress. NeedsApproval: the total is above the company's approval limit,
/// so placing it waits for someone with the approve right (unless the person placing it has that right). The
/// company's details are included for the printed order.
/// </summary>
public sealed record PurchaseOrderDto(
    int Id,
    string Number,
    PurchaseOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierGstin,
    AddressDto SupplierAddress,
    string? SupplierContactPerson,
    bool IsInterState,
    string? SupplierReference,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    string AmountInWords,
    bool NeedsApproval,
    decimal? ApprovalLimit,
    DateTime? SubmittedAtUtc,
    string? ApprovedByName,
    DateTime? ApprovedAtUtc,
    string? RejectionReason,
    DateTime? OrderedAtUtc,
    DateTime? ClosedAtUtc,
    string? CloseReason,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    bool IsLate,
    IReadOnlyList<PurchaseReceiptSummaryDto> Receipts,
    IReadOnlyList<PurchaseBillSummaryDto> Bills,
    string CompanyName,
    string CompanyGstin,
    string CompanyAddress,
    string? Terms,
    string CreatedByName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion);

public sealed record PurchaseOrderLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    public int ProductId { get; init; }

    [DecimalRange(0.001, 99999999, ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    /// <summary>Price per unit before GST. Defaults to the last price paid to this supplier, else the product's cost.</summary>
    [DecimalRange(0, 99999999, ErrorMessage = "Price must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal? UnitPrice { get; init; }
}

public sealed record PurchaseOrderSaveRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a supplier.")]
    public int SupplierId { get; init; }

    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? OrderDate { get; init; }

    public DateOnly? ExpectedDate { get; init; }

    [MaxLength(50)]
    public string? SupplierReference { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    [Required, MinLength(1, ErrorMessage = "Add at least one product."), MaxLength(200)]
    public List<PurchaseOrderLineInput> Lines { get; init; } = [];

    /// <summary>Required when updating.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Status changes carry the row version the user was looking at, so nobody acts on a stale screen.</summary>
public record PurchaseOrderActionRequest
{
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>Rejecting, cancelling or closing an order needs a reason, which is kept with it.</summary>
public sealed record PurchaseOrderReasonRequest : PurchaseOrderActionRequest
{
    [Required(ErrorMessage = "Give a reason."), MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record GoodsReceiptLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an order line.")]
    public int PurchaseOrderLineId { get; init; }

    /// <summary>Accepted into stock. Zero when everything delivered on this line was rejected.</summary>
    [DecimalRange(0, 99999999, ErrorMessage = "Quantity can't be negative."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    /// <summary>Refused on delivery: it doesn't enter stock, and the order still expects it.</summary>
    [DecimalRange(0, 99999999, ErrorMessage = "Rejected quantity can't be negative."), DecimalPlaces(3)]
    public decimal QuantityRejected { get; init; }

    [MaxLength(200)]
    public string? RejectionReason { get; init; }
}

/// <summary>Goods that arrived against a purchase order. They're costed at the order's price.</summary>
public sealed record GoodsReceiptCreateRequest
{
    /// <summary>Defaults to today (India time). Can't be in the future or before the order date.</summary>
    public DateOnly? ReceiptDate { get; init; }

    /// <summary>The supplier's delivery challan or invoice number.</summary>
    [MaxLength(50)]
    public string? SupplierReference { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }

    [Required, MinLength(1, ErrorMessage = "Enter what arrived."), MaxLength(200)]
    public List<GoodsReceiptLineInput> Lines { get; init; } = [];

    /// <summary>The order's row version, so a receipt never lands on an order that changed meanwhile.</summary>
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

// ---- Reordering -----------------------------------------------------------------------

/// <summary>
/// A product at or below its reorder level that open orders don't already cover, with a quantity to order, its
/// preferred supplier and the last price paid to them.
/// </summary>
public sealed record ReorderCandidateDto(
    int ProductId,
    string Sku,
    string Name,
    ProductUnit Unit,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal OnOrder,
    decimal SuggestedQuantity,
    int? SupplierId,
    string? SupplierName,
    decimal? LastPrice);

public sealed record ReorderDraftLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    public int ProductId { get; init; }

    [DecimalRange(0.001, 99999999, ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }
}

/// <summary>Products to reorder; each becomes a line on a draft order to its preferred supplier.</summary>
public sealed record ReorderDraftRequest
{
    [Required, MinLength(1, ErrorMessage = "Choose at least one product."), MaxLength(200)]
    public List<ReorderDraftLineInput> Lines { get; init; } = [];
}

// ---- Supplier bills -------------------------------------------------------------------

public sealed record SupplierBillListRequest : PageRequest
{
    public SupplierBillStatus? Status { get; init; }
    public BillMatchStatus? MatchStatus { get; init; }
    public int? SupplierId { get; init; }
    public int? PurchaseOrderId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Only bills with money still owed after their due date.</summary>
    public bool OverdueOnly { get; init; }

    /// <summary>Only bills with money still owed.</summary>
    public bool UnpaidOnly { get; init; }
}

public sealed record SupplierBillListItemDto(
    int Id,
    string Number,
    string SupplierInvoiceNumber,
    DateOnly BillDate,
    DateOnly DueDate,
    int SupplierId,
    string SupplierName,
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal AmountCredited,
    decimal Balance,
    SupplierBillStatus Status,
    BillMatchStatus MatchStatus,
    int? DaysOverdue);

/// <summary>A bill line. Issue is what the three-way check found wrong with it; QuantityReturned what went back since.</summary>
public sealed record SupplierBillLineDto(
    int Id,
    int LineNumber,
    int PurchaseOrderLineId,
    int ProductId,
    string Sku,
    string ProductName,
    string HsnCode,
    ProductUnit Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal OrderedPrice,
    decimal TaxableValue,
    decimal GstRate,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal LineTotal,
    string? Issue,
    decimal QuantityReturned);

public sealed record SupplierPaymentDto(
    int Id,
    int SupplierBillId,
    string BillNumber,
    string SupplierInvoiceNumber,
    int SupplierId,
    string SupplierName,
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMode Mode,
    string? Reference,
    string? Notes,
    string CreatedByName,
    DateTime CreatedAtUtc);

/// <summary>
/// A supplier bill. MatchIssues are the bill-level problems the three-way check found (lines carry their own); a bill
/// that needs approval can't be paid until someone with the right approves it.
/// </summary>
public sealed record SupplierBillDto(
    int Id,
    string Number,
    string FinancialYear,
    string SupplierInvoiceNumber,
    DateOnly BillDate,
    DateOnly DueDate,
    SupplierBillStatus Status,
    BillMatchStatus MatchStatus,
    BillSource Source,
    int SupplierId,
    string SupplierName,
    string? SupplierGstin,
    string SupplierStateCode,
    string SupplierStateName,
    bool IsInterState,
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    IReadOnlyList<SupplierBillLineDto> Lines,
    decimal TaxableValue,
    decimal ChargesAmount,
    decimal ChargesGstRate,
    string? ChargesDescription,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal? SupplierTotal,
    decimal AmountPaid,
    decimal AmountCredited,
    decimal Balance,
    int? DaysOverdue,
    IReadOnlyList<string> MatchIssues,
    string? ApprovedByName,
    DateTime? ApprovedAtUtc,
    string? ApprovalNote,
    string? Notes,
    IReadOnlyList<SupplierPaymentDto> Payments,
    IReadOnlyList<Returns.CreditUseDto> CreditsApplied,
    IReadOnlyList<Returns.ReturnSummaryDto> Returns,
    decimal SupplierCreditAvailable,
    string? CancelBlockedReason,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    string CreatedByName,
    DateTime CreatedAtUtc,
    string RowVersion);

public sealed record SupplierBillLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an order line.")]
    public int PurchaseOrderLineId { get; init; }

    [DecimalRange(0.001, 99999999, ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    [DecimalRange(0, 99999999, ErrorMessage = "Price must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal UnitPrice { get; init; }

    /// <summary>The GST rate printed on the bill. Defaults to the order line's rate.</summary>
    [DecimalRange(0, 28, ErrorMessage = "GST rate must be between 0% and 28%."), DecimalPlaces(2)]
    public decimal? GstRate { get; init; }
}

/// <summary>A supplier's invoice, entered against one of our purchase orders.</summary>
public sealed record SupplierBillCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the purchase order.")]
    public int PurchaseOrderId { get; init; }

    [Required(ErrorMessage = "Enter the supplier's invoice number."), MaxLength(30)]
    public string SupplierInvoiceNumber { get; init; } = string.Empty;

    /// <summary>The date on the supplier's invoice. Defaults to today.</summary>
    public DateOnly? BillDate { get; init; }

    /// <summary>Defaults to the bill date plus the supplier's payment terms.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>Freight, loading, packing and the like, before GST.</summary>
    [DecimalRange(0, 9999999, ErrorMessage = "Charges must be between 0 and 99,99,999."), DecimalPlaces(2)]
    public decimal ChargesAmount { get; init; }

    [DecimalRange(0, 28, ErrorMessage = "GST rate must be between 0% and 28%."), DecimalPlaces(2)]
    public decimal ChargesGstRate { get; init; }

    [MaxLength(100)]
    public string? ChargesDescription { get; init; }

    /// <summary>The grand total printed on the supplier's invoice, to check ours against.</summary>
    [DecimalRange(0, 999999999), DecimalPlaces(2)]
    public decimal? SupplierTotal { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }

    /// <summary>Manual, or AiPdf / AiImage when the bill was read by AI.</summary>
    public BillSource Source { get; init; } = BillSource.Manual;

    [Required, MinLength(1, ErrorMessage = "Add at least one line."), MaxLength(200)]
    public List<SupplierBillLineInput> Lines { get; init; } = [];
}

public sealed record SupplierBillApproveRequest
{
    [Required]
    public string RowVersion { get; init; } = string.Empty;

    /// <summary>Why the differences are acceptable; kept with the bill.</summary>
    [Required(ErrorMessage = "Say why the differences are acceptable."), MaxLength(500)]
    public string Note { get; init; } = string.Empty;
}

public sealed record SupplierBillCancelRequest
{
    [Required(ErrorMessage = "Give a reason for cancelling."), MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>Payables at a glance.</summary>
public sealed record SupplierBillSummaryDto(
    decimal Payable,
    int OpenBills,
    decimal Overdue,
    int OverdueBills,
    decimal DueNextSevenDays,
    int NeedsApproval,
    decimal PaidThisMonth);

// ---- Supplier payments ----------------------------------------------------------------

public sealed record SupplierPaymentListRequest : PageRequest
{
    public PaymentMode? Mode { get; init; }
    public int? SupplierId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record SupplierPaymentCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a bill.")]
    public int SupplierBillId { get; init; }

    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? PaymentDate { get; init; }

    [DecimalRange(0.01, 999999999, ErrorMessage = "Amount must be more than zero."), DecimalPlaces(2)]
    public decimal Amount { get; init; }

    public PaymentMode Mode { get; init; }

    [MaxLength(50)]
    public string? Reference { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }
}

// ---- Reading bills with AI ------------------------------------------------------------

/// <summary>
/// One line read from a supplier's bill, matched to a line of the purchase order when Claude could. The quantity,
/// price and GST rate are as printed on the bill; OrderedPrice is the order's, for comparison.
/// </summary>
public sealed record BillExtractedLineDto(
    string SourceText,
    int? PurchaseOrderLineId,
    int? ProductId,
    string? Sku,
    string? ProductName,
    ProductUnit? Unit,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? GstRate,
    decimal? OrderedPrice,
    ExtractionConfidence Confidence,
    string? Note);

/// <summary>
/// A bill read from a PDF or photo, ready for the user to check before saving it. Nothing has been saved. Warnings
/// are what the checks found: a GSTIN that isn't the supplier's, an invoice number already entered, totals that don't
/// add up.
/// </summary>
public sealed record BillExtractionDto(
    BillSource Source,
    bool LooksLikeInvoice,
    string? SupplierName,
    string? SupplierGstin,
    string? SupplierInvoiceNumber,
    DateOnly? BillDate,
    DateOnly? DueDate,
    string? PurchaseOrderNumber,
    IReadOnlyList<BillExtractedLineDto> Lines,
    decimal ChargesAmount,
    decimal ChargesGstRate,
    string? ChargesDescription,
    decimal? StatedTaxableValue,
    decimal? StatedTaxAmount,
    decimal? StatedTotal,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Unmatched,
    string? Notes,
    AiUsageDto Usage);
