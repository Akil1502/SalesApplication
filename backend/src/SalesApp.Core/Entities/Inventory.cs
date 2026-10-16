namespace SalesApp.Core.Entities;

/// <summary>
/// Current stock of one product. Kept in its own row (not on <see cref="Product"/>) so the frequent,
/// atomic stock updates from receipts and invoices never clash with someone editing the product's price.
/// </summary>
public sealed class StockLevel
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Always equals the sum of the product's <see cref="StockMovement"/> quantities.</summary>
    public decimal QuantityOnHand { get; set; }

    public DateTime? LastMovementAtUtc { get; set; }
}

public enum StockMovementType
{
    /// <summary>Stock already on the shelf when the product was set up.</summary>
    Opening,

    /// <summary>Goods received (purchase, production, transfer in).</summary>
    Receipt,

    /// <summary>Manual correction: damage, loss, stock count difference.</summary>
    Adjustment,

    /// <summary>Goods out on an invoice (phase P3).</summary>
    Sale,

    /// <summary>A cancelled invoice putting its goods back (phase P3).</summary>
    SaleReversal,

    /// <summary>Goods a customer sent back, on a credit note.</summary>
    SaleReturn,

    /// <summary>Goods sent back to a supplier, on a debit note.</summary>
    PurchaseReturn,
}

public enum AdjustmentReason
{
    OpeningBalance,
    StockCount,
    Damaged,
    Lost,
    Found,
    Other,
}

/// <summary>
/// One line of the stock ledger. Insert-only: mistakes are corrected with a new adjustment, never by
/// editing history, so the ledger always explains how the current quantity came about.
/// </summary>
public sealed class StockMovement
{
    public long Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>The business date the movement belongs to (goods received on, invoice dated), in India time.</summary>
    public DateOnly MovementDate { get; set; }

    /// <summary>Signed: positive adds stock, negative removes it.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Stock on hand immediately after this movement, captured atomically when it was posted.</summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>Cost per unit for receipts; used for stock valuation.</summary>
    public decimal? UnitCost { get; set; }

    public AdjustmentReason? Reason { get; set; }

    /// <summary>What caused the movement, e.g. "Receipt" or "Invoice", with its id and printable number.</summary>
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A goods receipt note (GRN): one delivery against a purchase order, possibly several products. Each line is also a
/// stock movement with ReferenceType "Receipt" and ReferenceId = this Id. There's deliberately no foreign key on the
/// movement: ReferenceId also points at invoices, so it can't be constrained to one table.
/// </summary>
public sealed class StockReceipt
{
    public int Id { get; set; }

    /// <summary>Our reference, e.g. GRN-00012. Generated from a database sequence.</summary>
    public required string Number { get; set; }

    public DateOnly ReceiptDate { get; set; }

    /// <summary>The order the goods came in against. Null only for receipts recorded before purchase orders existed.</summary>
    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>The supplier's name as it was on the day (older receipts have only this).</summary>
    public string? SupplierName { get; set; }

    /// <summary>The supplier's invoice or delivery challan number.</summary>
    public string? SupplierReference { get; set; }

    public string? Notes { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<StockReceiptLine> Lines { get; set; } = [];
}

/// <summary>Values for <see cref="StockMovement.ReferenceType"/>.</summary>
public static class StockReferenceTypes
{
    public const string Receipt = "Receipt";
    public const string Invoice = "Invoice";
    public const string CreditNote = "CreditNote";
    public const string SupplierReturn = "SupplierReturn";
}
