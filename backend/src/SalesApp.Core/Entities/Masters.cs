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

    public byte[] RowVersion { get; set; } = [];
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

    /// <summary>Purchase/production cost per unit. Visible to managers only (margins).</summary>
    public decimal? CostPrice { get; set; }

    public decimal ReorderLevel { get; set; }
    public decimal ReorderQuantity { get; set; }
    public int LeadTimeDays { get; set; }

    /// <summary>
    /// Cached stock on hand. Maintained by the stock ledger (phase P2), never edited directly,
    /// so the product form doesn't expose it.
    /// </summary>
    public decimal QuantityOnHand { get; set; }

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
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
