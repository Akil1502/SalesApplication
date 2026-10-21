namespace SalesApp.Core.Entities;

/// <summary>Entities that carry created/updated timestamps, stamped automatically on save.</summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// An Indian state or union territory, keyed by its two-digit GST state code (the first two
/// characters of every GSTIN registered there). Reference data, seeded by migration.
/// </summary>
public sealed class State
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Union territories without a legislature charge UTGST instead of SGST on intra-state supplies.
    /// Delhi, Puducherry and Jammu &amp; Kashmir have legislatures, so they use SGST like states.
    /// </summary>
    public bool UsesUtgst { get; set; }
}

/// <summary>The selling company: printed on every invoice and used as the origin for GST place-of-supply rules.</summary>
public sealed class CompanySettings
{
    /// <summary>Always 1; there is exactly one row.</summary>
    public int Id { get; set; }

    public required string LegalName { get; set; }
    public string? TradeName { get; set; }
    public required string Gstin { get; set; }
    public required string StateCode { get; set; }
    public State? State { get; set; }

    public required string AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public required string City { get; set; }
    public required string Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }

    /// <summary>Prefixes for document numbers, e.g. INV/25-26/0001. Kept short: invoice numbers are capped at 16 characters.</summary>
    public required string InvoicePrefix { get; set; }
    public required string QuotationPrefix { get; set; }

    /// <summary>Terms and conditions printed at the foot of invoices.</summary>
    public string? InvoiceTerms { get; set; }

    /// <summary>Terms printed at the foot of purchase orders.</summary>
    public string? PurchaseOrderTerms { get; set; }

    /// <summary>Purchase orders above this total (with GST) need someone who can approve them. Null: no approvals.</summary>
    public decimal? PurchaseApprovalLimit { get; set; }

    /// <summary>How much more than ordered may be received, as a percentage (yarn and fabric rarely arrive exact).</summary>
    public decimal OverReceiptTolerancePercent { get; set; }

    /// <summary>How far a bill's price may differ from the order's before the bill needs approval, as a percentage.</summary>
    public decimal BillPriceTolerancePercent { get; set; }

    /// <summary>
    /// The colour the app is drawn in, as #rrggbb; null keeps the standard indigo. Any colour is allowed: the browser
    /// adjusts its shades wherever text on it would be hard to read.
    /// </summary>
    public string? BrandColour { get; set; }

    /// <summary>The language of printed documents for customers and suppliers who haven't chosen their own.</summary>
    public DocumentLanguage DocumentLanguage { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// The company's logo, for the menu, the sign-in page, the browser tab and printed documents. At most one row, in
/// its own table so reading the company settings never drags the picture along. Removing the logo keeps the row
/// (without a picture) so the version keeps counting up: a logo added later never reuses an address a browser has
/// cached.
/// </summary>
public sealed class CompanyLogo
{
    /// <summary>Always 1.</summary>
    public int Id { get; set; }

    /// <summary>image/png, image/jpeg or image/webp, read from the file's first bytes; null once removed.</summary>
    public string? ContentType { get; set; }

    public byte[]? ImageData { get; set; }

    /// <summary>Goes up with every change; the logo's address carries it, so browsers can cache each one for good.</summary>
    public int Version { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A Harmonised System of Nomenclature code and the GST rate that applies to it. Rates live here as data
/// (not in code) because the government revises them; invoices copy the rate onto each line when issued.
/// </summary>
public sealed class HsnCode
{
    public int Id { get; set; }

    /// <summary>4, 6 or 8 digits.</summary>
    public required string Code { get; set; }

    public required string Description { get; set; }

    /// <summary>Total GST percentage (e.g. 5 means CGST 2.5% + SGST 2.5%, or IGST 5%).</summary>
    public decimal GstRate { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The language a customer's or supplier's documents are printed in (invoices, quotations, credit and debit notes,
/// purchase orders) and their payment reminders written in. Both prints each label in English with the Tamil under it.
/// </summary>
public enum DocumentLanguage
{
    English,
    Tamil,
    Both,
}

public enum ProductUnit
{
    Kg,
    Metre,
    Piece,
    Cone,
    Bale,
    Box,
    Roll,
    Set,
}

public sealed class Product : IAuditable
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int HsnCodeId { get; set; }
    public HsnCode? HsnCode { get; set; }

    public ProductUnit Unit { get; set; }

    /// <summary>Selling price per unit, excluding GST.</summary>
    public decimal ListPrice { get; set; }

    /// <summary>
    /// Cost per unit: the weighted average of what stock cost to bring in (receipts at the order price, then the bill's
    /// price differences and freight). Can also be set by hand. Visible only with the cost prices right.
    /// </summary>
    public decimal? CostPrice { get; set; }

    /// <summary>Who to buy from by default: reorder suggestions become draft orders with this supplier.</summary>
    public int? PreferredSupplierId { get; set; }
    public Supplier? PreferredSupplier { get; set; }

    public decimal ReorderLevel { get; set; }
    public decimal ReorderQuantity { get; set; }
    public int LeadTimeDays { get; set; }

    /// <summary>Stock on hand lives in its own table, maintained only by the stock ledger.</summary>
    public StockLevel? Stock { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A postal address; stored inline on its owner (owned entity), not in its own table.</summary>
public sealed class Address
{
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public required string StateCode { get; set; }
    public required string Pincode { get; set; }
}

public sealed class Customer : IAuditable
{
    public int Id { get; set; }

    /// <summary>Short reference such as C00042. Generated from a database sequence when not supplied.</summary>
    public required string Code { get; set; }

    /// <summary>Legal name, as registered for GST.</summary>
    public required string Name { get; set; }
    public string? TradeName { get; set; }

    /// <summary>Null for unregistered (B2C) customers.</summary>
    public string? Gstin { get; set; }

    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public required Address BillingAddress { get; set; }

    /// <summary>Null when goods ship to the billing address.</summary>
    public Address? ShippingAddress { get; set; }

    /// <summary>Maximum outstanding amount allowed; null means no limit has been set.</summary>
    public decimal? CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; } = 30;

    public Guid? AssignedRepId { get; set; }
    public AppUser? AssignedRep { get; set; }

    public string? Notes { get; set; }

    /// <summary>Null: the company's default (Company settings).</summary>
    public DocumentLanguage? DocumentLanguage { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
