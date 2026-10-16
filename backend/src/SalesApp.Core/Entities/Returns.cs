namespace SalesApp.Core.Entities;

/// <summary>Whether goods came back, or only the value of the invoice is being corrected.</summary>
public enum CreditNoteKind
{
    /// <summary>The customer sent goods back. Each line says whether they go back into stock or were damaged.</summary>
    GoodsReturn,

    /// <summary>No goods move: a wrong rate, short supply or a discount agreed after the sale.</summary>
    PriceCorrection,
}

/// <summary>Why goods came back or the value was corrected. Supplier returns use the goods reasons only.</summary>
public enum ReturnReason
{
    Damaged,
    WrongItem,
    QualityIssue,
    ExcessSupplied,
    RateDifference,
    ShortSupply,
    Discount,
    Other,
}

/// <summary>What happened to a returned line's goods.</summary>
public enum ReturnDisposition
{
    /// <summary>Fit to sell again: back into stock at the product's cost.</summary>
    Restock,

    /// <summary>Recorded as returned and written off at once, so the loss shows in the stock ledger.</summary>
    Damaged,
}

public enum ReturnDocumentStatus
{
    Issued,
    Cancelled,
}

/// <summary>
/// A GST credit note (CGST Act s.34) against one invoice, for goods returned or a value correction. Everything printed
/// on it is copied from the invoice, so it reads the same forever. Its value first comes off what's still due on that
/// invoice; anything left is credit on the customer's account, set against their other invoices or refunded.
/// </summary>
public sealed class CreditNote
{
    public int Id { get; set; }

    /// <summary>e.g. CN/26-27/00001: its own gapless series per financial year.</summary>
    public required string Number { get; set; }

    /// <summary>Start year of the financial year of the credit note date.</summary>
    public int FinancialYear { get; set; }

    public DateOnly CreditNoteDate { get; set; }
    public ReturnDocumentStatus Status { get; set; } = ReturnDocumentStatus.Issued;
    public CreditNoteKind Kind { get; set; }
    public ReturnReason Reason { get; set; }
    public string? ReasonNote { get; set; }

    /// <summary>
    /// False for a "financial" credit note, issued after the s.34(2) deadline (30 November after the invoice's financial
    /// year): it credits the value only, reduces no GST, and the GST reports leave it out.
    /// </summary>
    public bool WithGst { get; set; } = true;

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Seller and buyer, as on the invoice.
    public required string SellerName { get; set; }
    public required string SellerGstin { get; set; }
    public required string SellerStateCode { get; set; }
    public required string SellerAddress { get; set; }
    public required string CustomerName { get; set; }
    public string? CustomerGstin { get; set; }
    public required InvoiceAddress BillingAddress { get; set; }
    public required string PlaceOfSupplyStateCode { get; set; }
    public required string PlaceOfSupplyStateName { get; set; }
    public bool IsInterState { get; set; }
    public bool UsesUtgst { get; set; }

    public decimal TaxableValue { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>Set against invoices: its own first, then any others of the customer's.</summary>
    public decimal AmountAdjusted { get; set; }

    /// <summary>Paid back to the customer.</summary>
    public decimal AmountRefunded { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<CreditNoteLine> Lines { get; set; } = [];
    public List<CreditAdjustment> Adjustments { get; set; } = [];
    public List<CreditRefund> Refunds { get; set; } = [];
}

public sealed class CreditNoteLine
{
    public int Id { get; set; }
    public int CreditNoteId { get; set; }
    public int LineNumber { get; set; }

    public int InvoiceLineId { get; set; }
    public int ProductId { get; set; }
    public required string ProductSku { get; set; }
    public required string ProductName { get; set; }
    public required string HsnCode { get; set; }
    public ProductUnit Unit { get; set; }

    /// <summary>Goods returned; null on a price correction.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Null on a price correction.</summary>
    public ReturnDisposition? Disposition { get; set; }

    public decimal TaxableValue { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>Part of a credit note set against an invoice of the same customer, reducing what's due on it.</summary>
public sealed class CreditAdjustment
{
    public int Id { get; set; }

    public int CreditNoteId { get; set; }
    public CreditNote? CreditNote { get; set; }

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateOnly AdjustmentDate { get; set; }
    public decimal Amount { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Credit paid back to the customer.</summary>
public sealed class CreditRefund
{
    public int Id { get; set; }

    public int CreditNoteId { get; set; }
    public CreditNote? CreditNote { get; set; }

    public DateOnly RefundDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMode Mode { get; set; }

    /// <summary>UTR, UPI transaction id or cheque number.</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Goods sent back to a supplier against one of their bills: our debit note. Stock goes out at the bill's price, what
/// we owe on that bill drops, and its input GST is reversed. Anything beyond what's still due is credit with the
/// supplier, set against their other bills or refunded to us. The supplier's own credit note number is noted when it
/// arrives.
/// </summary>
public sealed class SupplierReturn
{
    public int Id { get; set; }

    /// <summary>e.g. DN/26-27/00001: its own gapless series per financial year.</summary>
    public required string Number { get; set; }

    public int FinancialYear { get; set; }
    public DateOnly ReturnDate { get; set; }
    public ReturnDocumentStatus Status { get; set; } = ReturnDocumentStatus.Issued;
    public ReturnReason Reason { get; set; }
    public string? ReasonNote { get; set; }

    /// <summary>False after the s.34(2) deadline for the bill: value only, no input GST reversed.</summary>
    public bool WithGst { get; set; } = true;

    public int SupplierBillId { get; set; }
    public SupplierBill? SupplierBill { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    // The supplier as on the bill.
    public required string SupplierName { get; set; }
    public string? SupplierGstin { get; set; }
    public required string SupplierStateCode { get; set; }
    public bool IsInterState { get; set; }

    /// <summary>The supplier's credit note for these goods, once it arrives.</summary>
    public string? SupplierCreditNoteNumber { get; set; }
    public DateOnly? SupplierCreditNoteDate { get; set; }

    public decimal TaxableValue { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>Set against the supplier's bills: this one first, then others.</summary>
    public decimal AmountAdjusted { get; set; }

    /// <summary>Refunded to us by the supplier.</summary>
    public decimal AmountRefunded { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<SupplierReturnLine> Lines { get; set; } = [];
    public List<SupplierCreditAdjustment> Adjustments { get; set; } = [];
    public List<SupplierRefund> Refunds { get; set; } = [];
}

public sealed class SupplierReturnLine
{
    public int Id { get; set; }
    public int SupplierReturnId { get; set; }
    public int LineNumber { get; set; }

    public int SupplierBillLineId { get; set; }
    public int ProductId { get; set; }
    public required string ProductSku { get; set; }
    public required string ProductName { get; set; }
    public required string HsnCode { get; set; }
    public ProductUnit Unit { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>The bill's price per unit, before GST: what the goods go out of stock at.</summary>
    public decimal UnitPrice { get; set; }

    public decimal TaxableValue { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>Part of a supplier return set against one of the supplier's bills, reducing what we owe on it.</summary>
public sealed class SupplierCreditAdjustment
{
    public int Id { get; set; }

    public int SupplierReturnId { get; set; }
    public SupplierReturn? SupplierReturn { get; set; }

    public int SupplierBillId { get; set; }
    public SupplierBill? SupplierBill { get; set; }

    public DateOnly AdjustmentDate { get; set; }
    public decimal Amount { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Money the supplier paid back to us for returned goods.</summary>
public sealed class SupplierRefund
{
    public int Id { get; set; }

    public int SupplierReturnId { get; set; }
    public SupplierReturn? SupplierReturn { get; set; }

    public DateOnly RefundDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMode Mode { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
