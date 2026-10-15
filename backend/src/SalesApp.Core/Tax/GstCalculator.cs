namespace SalesApp.Core.Tax;

/// <summary>One line to tax: quantity × price, less a percentage discount, at a GST rate.</summary>
public sealed record GstLineInput(decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal GstRate);

/// <summary>
/// Tax on one line. For an inter-state supply only IGST is charged; within a state the rate is split
/// equally into CGST and SGST (UTGST in union territories without a legislature, carried in the SGST fields).
/// </summary>
public sealed record GstLineResult(
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal TaxableValue,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount)
{
    public decimal TaxAmount => CgstAmount + SgstAmount + IgstAmount;
    public decimal LineTotal => TaxableValue + TaxAmount;
}

public sealed record GstTotals(
    IReadOnlyList<GstLineResult> Lines,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal)
{
    public decimal TaxAmount => CgstAmount + SgstAmount + IgstAmount;
}

/// <summary>
/// GST arithmetic for quotations and invoices. Every amount is rounded to the paisa at line level
/// (half away from zero, as on a calculator); the invoice total is then rounded to the nearest rupee
/// and the difference shown as "round off", which is how Indian invoices are normally presented.
/// The same rules are mirrored in the browser (frontend/src/lib/gst.ts) for live previews.
/// </summary>
public static class GstCalculator
{
    public static GstTotals Calculate(IEnumerable<GstLineInput> lines, bool isInterState)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var results = lines.Select(line => CalculateLine(line, isInterState)).ToList();

        var taxable = results.Sum(r => r.TaxableValue);
        var cgst = results.Sum(r => r.CgstAmount);
        var sgst = results.Sum(r => r.SgstAmount);
        var igst = results.Sum(r => r.IgstAmount);
        var exact = taxable + cgst + sgst + igst;
        var grandTotal = Math.Round(exact, 0, MidpointRounding.AwayFromZero);

        return new GstTotals(results, taxable, cgst, sgst, igst, grandTotal - exact, grandTotal);
    }

    public static GstLineResult CalculateLine(GstLineInput line, bool isInterState)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(line), "Quantity must be positive.");
        if (line.UnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(line), "Unit price can't be negative.");
        if (line.DiscountPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(line), "Discount must be 0-100%.");
        if (line.GstRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(line), "GST rate must be 0-100%.");

        var gross = Money(line.Quantity * line.UnitPrice);
        var discount = Money(gross * line.DiscountPercent / 100m);
        var taxable = gross - discount;

        if (isInterState)
        {
            return new GstLineResult(gross, discount, taxable, 0m, 0m, 0m, 0m, line.GstRate, Money(taxable * line.GstRate / 100m));
        }

        // Each half is rounded on its own, so CGST and SGST are always equal on the invoice.
        var halfRate = line.GstRate / 2m;
        var halfTax = Money(taxable * halfRate / 100m);
        return new GstLineResult(gross, discount, taxable, halfRate, halfTax, halfRate, halfTax, 0m, 0m);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
