namespace SalesApp.Core.Entities;

/// <summary>Someone the business buys from. GST on their bills is IGST or CGST + SGST depending on their state.</summary>
public sealed class Supplier : IAuditable
{
    public int Id { get; set; }

    /// <summary>Short reference such as S00007. Generated from a database sequence when not supplied.</summary>
    public required string Code { get; set; }

    /// <summary>Legal name, as registered for GST.</summary>
    public required string Name { get; set; }
    public string? TradeName { get; set; }

    /// <summary>Null for unregistered suppliers (no input tax credit can be claimed on their bills).</summary>
    public string? Gstin { get; set; }

    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>The GST-registered address; its state decides whether their bills carry IGST.</summary>
    public required Address Address { get; set; }

    /// <summary>How many days after the bill date we pay them.</summary>
    public int PaymentTermsDays { get; set; } = 30;

    // Where to pay them.
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Draft → Ordered (straight away, or after approval when above the company's limit) → PartlyReceived → Received.
/// An order can be cancelled before anything arrives, or closed short once part of it has.
/// </summary>
public enum PurchaseOrderStatus
{
    Draft,
    PendingApproval,
    Ordered,
    PartlyReceived,
    Received,
    Closed,
    Cancelled,
}

/// <summary>An order placed with a supplier. Goods can only be received against one (see <see cref="StockReceipt"/>).</summary>
public sealed class PurchaseOrder : IAuditable
{
    public int Id { get; set; }

    /// <summary>e.g. PO/26-27/00042, numbered per financial year.</summary>
    public required string Number { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public DateOnly OrderDate { get; set; }

    /// <summary>When the supplier promised delivery; past it with goods still due, the order is late.</summary>
    public DateOnly? ExpectedDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Whether the supplier is in another state (IGST) as of the last save; bills work it out again.</summary>
    public bool IsInterState { get; set; }

    /// <summary>The supplier's quotation or reference.</summary>
    public string? SupplierReference { get; set; }
    public string? Notes { get; set; }

    // Estimates at the GST rates when last saved; the supplier's bill is what counts.
    public decimal TaxableValue { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>Why the approver sent it back to draft, until it's placed again.</summary>
    public string? RejectionReason { get; set; }

    public DateTime? OrderedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? CloseReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Agreed price per unit before GST. Goods are costed at it when received.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>The product's GST rate when the line was saved.</summary>
    public decimal GstRate { get; set; }

    /// <summary>Running totals kept by goods receipts and supplier bills, for matching and "still to come".</summary>
    public decimal QuantityReceived { get; set; }
    public decimal QuantityBilled { get; set; }
}

/// <summary>One line of a goods receipt: what arrived, against which order line, at what cost.</summary>
public sealed class StockReceiptLine
{
    public int Id { get; set; }
    public int StockReceiptId { get; set; }

    /// <summary>Null only for receipts recorded before purchase orders existed.</summary>
    public int? PurchaseOrderLineId { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
}

public enum SupplierBillStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Cancelled,
}

/// <summary>The three-way check of a bill against its order (price) and what was received (quantity).</summary>
public enum BillMatchStatus
{
    /// <summary>Everything within tolerance: ready to pay.</summary>
    Matched,

    /// <summary>Something differs by more than the tolerance; it can't be paid until someone approves it.</summary>
    NeedsApproval,

    /// <summary>Differences accepted by someone with the right to approve them.</summary>
    Approved,
}

/// <summary>How the bill was entered.</summary>
public enum BillSource
{
    Manual,
    AiPdf,
    AiImage,
}

/// <summary>
/// A supplier's tax invoice, entered against a purchase order. Its GST is the input tax credit claimed. Never deleted:
/// a wrong bill is cancelled and stays on record.
/// </summary>
public sealed class SupplierBill
{
    public int Id { get; set; }

    /// <summary>Our reference, e.g. PB/26-27/00042.</summary>
    public required string Number { get; set; }

    /// <summary>Start year of the financial year of the bill date.</summary>
    public int FinancialYear { get; set; }

    /// <summary>The supplier's invoice number and date, as printed on their bill.</summary>
    public required string SupplierInvoiceNumber { get; set; }
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }

    public SupplierBillStatus Status { get; set; } = SupplierBillStatus.Unpaid;
    public BillMatchStatus MatchStatus { get; set; } = BillMatchStatus.Matched;
    public BillSource Source { get; set; } = BillSource.Manual;

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    // The supplier as on the bill, so later edits to the supplier don't rewrite it.
    public required string SupplierName { get; set; }
    public string? SupplierGstin { get; set; }
    public required string SupplierStateCode { get; set; }
    public bool IsInterState { get; set; }

    /// <summary>Goods value on the lines, excluding GST.</summary>
    public decimal TaxableValue { get; set; }

    /// <summary>Freight, loading, packing and the like, excluding GST. Spread over the goods' cost.</summary>
    public decimal ChargesAmount { get; set; }
    public decimal ChargesGstRate { get; set; }
    public string? ChargesDescription { get; set; }

    // GST on goods and charges together.
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>The total printed on the supplier's invoice, when given; a difference is flagged.</summary>
    public decimal? SupplierTotal { get; set; }

    /// <summary>Bill-level differences found by the check (lines carry their own), as JSON.</summary>
    public string? MatchIssuesJson { get; set; }

    public Guid? ApprovedByUserId { get; set; }
    public AppUser? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovalNote { get; set; }

    public string? Notes { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<SupplierBillLine> Lines { get; set; } = [];
    public List<SupplierPayment> Payments { get; set; } = [];
}

public sealed class SupplierBillLine
{
    public int Id { get; set; }
    public int SupplierBillId { get; set; }
    public int LineNumber { get; set; }

    public int PurchaseOrderLineId { get; set; }

    public int ProductId { get; set; }
    public required string ProductSku { get; set; }
    public required string ProductName { get; set; }
    public required string HsnCode { get; set; }
    public ProductUnit Unit { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>The order's price for this line when the bill was entered, for the price check.</summary>
    public decimal OrderedPrice { get; set; }

    public decimal TaxableValue { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>What the check found wrong with this line, if anything.</summary>
    public string? Issue { get; set; }
}

/// <summary>A payment to a supplier against one bill.</summary>
public sealed class SupplierPayment
{
    public int Id { get; set; }

    public int SupplierBillId { get; set; }
    public SupplierBill? SupplierBill { get; set; }

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
