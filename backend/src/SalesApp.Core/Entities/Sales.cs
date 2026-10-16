namespace SalesApp.Core.Entities;

/// <summary>
/// Draft → Quoted → Confirmed → Invoiced, or Cancelled from any state before Invoiced. Cancelling an
/// invoice puts its order back to Confirmed, so it can be re-invoiced or cancelled.
/// </summary>
public enum SalesOrderStatus
{
    Draft,
    Quoted,
    Confirmed,
    Invoiced,
    Cancelled,
}

/// <summary>How the order was captured. The AI sources arrive with smart order entry (phase P5).</summary>
public enum OrderSource
{
    Manual,
    AiText,
    AiPdf,
    AiImage,
}

/// <summary>A quotation that becomes an order once the customer accepts it. One document, one lifecycle.</summary>
public sealed class SalesOrder : IAuditable
{
    public int Id { get; set; }

    /// <summary>e.g. SO/26-27/00042, numbered per financial year.</summary>
    public required string Number { get; set; }

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public OrderSource Source { get; set; } = OrderSource.Manual;

    public DateOnly OrderDate { get; set; }

    /// <summary>How long the quoted prices hold.</summary>
    public DateOnly? ValidUntil { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>
    /// GST place of supply: where the goods are delivered (the shipping state, else the billing state).
    /// Compared with the company's state, it decides CGST + SGST versus IGST.
    /// </summary>
    public required string PlaceOfSupplyStateCode { get; set; }

    /// <summary>The customer's own purchase order number.</summary>
    public string? CustomerReference { get; set; }
    public string? Notes { get; set; }

    /// <summary>Assigned when the order is marked as quoted, e.g. QT/26-27/00007.</summary>
    public string? QuotationNumber { get; set; }

    public DateTime? QuotedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>The current (non-cancelled) invoice, if any.</summary>
    public int? InvoiceId { get; set; }

    // Totals at the GST rates current when the order was last saved; the invoice recalculates at issue.
    public decimal TaxableValue { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<SalesOrderLine> Lines { get; set; } = [];
}

public sealed class SalesOrderLine
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int LineNumber { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }

    /// <summary>The product's GST rate when the line was saved (for the quotation; invoices re-read it).</summary>
    public decimal GstRate { get; set; }
}

public enum InvoiceStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Cancelled,
}

/// <summary>An address frozen onto an invoice, so later edits to the customer don't rewrite history.</summary>
public sealed class InvoiceAddress
{
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public required string StateCode { get; set; }
    public required string StateName { get; set; }
    public required string Pincode { get; set; }
}

/// <summary>
/// A tax invoice. Everything printed on it (seller, buyer, products, rates) is copied at issue, so the
/// invoice reads the same forever even if masters change. Never deleted: cancelling keeps it in the
/// register with its number, which is what GST requires.
/// </summary>
public sealed class Invoice
{
    public int Id { get; set; }

    /// <summary>e.g. INV/26-27/00001. Consecutive within a financial year with no gaps.</summary>
    public required string Number { get; set; }

    /// <summary>Start year of the financial year (2026 for FY 2026-27).</summary>
    public int FinancialYear { get; set; }

    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

    public int SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Seller, as printed.
    public required string SellerName { get; set; }
    public required string SellerGstin { get; set; }
    public required string SellerStateCode { get; set; }
    public required string SellerAddress { get; set; }

    // Buyer, as printed.
    public required string CustomerName { get; set; }
    public string? CustomerGstin { get; set; }
    public required InvoiceAddress BillingAddress { get; set; }
    public InvoiceAddress? ShippingAddress { get; set; }

    public required string PlaceOfSupplyStateCode { get; set; }
    public required string PlaceOfSupplyStateName { get; set; }
    public bool IsInterState { get; set; }

    /// <summary>Intra-state supply in a union territory without a legislature: the SGST share is labelled UTGST.</summary>
    public bool UsesUtgst { get; set; }

    public string? CustomerReference { get; set; }

    public decimal TaxableValue { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>Credit notes set against this invoice. What's due is GrandTotal - AmountPaid - AmountCredited.</summary>
    public decimal AmountCredited { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<InvoiceLine> Lines { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public sealed class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int LineNumber { get; set; }

    public int ProductId { get; set; }
    public required string ProductSku { get; set; }
    public required string ProductName { get; set; }
    public required string HsnCode { get; set; }
    public ProductUnit Unit { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public enum PaymentMode
{
    Cash,
    Upi,
    Neft,
    Rtgs,
    Cheque,
    Card,
    Other,
}

public sealed class Payment
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateOnly PaymentDate { get; set; }
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
/// Next number per document type and financial year. Incremented inside the same transaction that
/// creates the document, so a rolled-back invoice also rolls back its number: no gaps, unlike SQL sequences.
/// </summary>
public sealed class DocumentSequence
{
    public required string DocumentType { get; set; }
    public int FinancialYear { get; set; }
    public int NextNumber { get; set; } = 1;
}

/// <summary>Values for <see cref="DocumentSequence.DocumentType"/>.</summary>
public static class DocumentTypes
{
    public const string SalesOrder = "SalesOrder";
    public const string Quotation = "Quotation";
    public const string Invoice = "Invoice";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string SupplierBill = "SupplierBill";
    public const string CreditNote = "CreditNote";
    public const string DebitNote = "DebitNote";
}
