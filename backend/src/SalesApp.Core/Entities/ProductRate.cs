namespace SalesApp.Core.Entities;

/// <summary>
/// A product's selling rate for a period, such as January's rate list (1 Jan to 31 Jan). An order is priced at the
/// rate whose period covers its order date; outside every period the product's list price applies. A product's
/// periods never overlap. Orders and invoices copy the price onto their lines, so changing a rate later never
/// changes them.
/// </summary>
public sealed class ProductRate : IAuditable
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Per unit, excluding GST, like the list price.</summary>
    public decimal Rate { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>The last day the rate applies; null means until a later period replaces it.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public string? Note { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
