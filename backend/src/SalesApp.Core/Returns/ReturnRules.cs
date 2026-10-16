using SalesApp.Core.Common;
using SalesApp.Core.Tax;

namespace SalesApp.Core.Returns;

/// <summary>A line of an invoice or supplier bill as it was issued: what a credit or return can take back.</summary>
public sealed record CreditableLine(decimal Quantity, decimal TaxableValue, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal GstRate);

/// <summary>What earlier credit notes or returns have already taken back from the line.</summary>
public sealed record CreditedSoFar(decimal Quantity, decimal TaxableValue, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount)
{
    public static readonly CreditedSoFar None = new(0m, 0m, 0m, 0m, 0m);
}

/// <summary>The amounts on one credit note or return line.</summary>
public sealed record CreditAmounts(decimal TaxableValue, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount)
{
    public decimal LineTotal => TaxableValue + CgstAmount + SgstAmount + IgstAmount;
}

/// <summary>
/// The rules credit notes and supplier returns share. A credit never takes back more than the line was worth, so the
/// last piece of a line takes exactly what's left (no paisa lost or gained to rounding across several returns).
/// </summary>
public static class ReturnRules
{
    /// <summary>
    /// The last day a credit note can reduce GST on a supply made on <paramref name="documentDate"/>: 30 November after
    /// the end of that financial year (CGST Act s.34(2); the annual return date can come earlier, which isn't tracked).
    /// </summary>
    public static DateOnly GstDeadline(DateOnly documentDate) => new(FinancialYear.StartYear(documentDate) + 1, 11, 30);

    /// <summary>Whether a credit dated <paramref name="creditDate"/> can still reduce the GST of that supply.</summary>
    public static bool CarriesGst(DateOnly documentDate, DateOnly creditDate) => creditDate <= GstDeadline(documentDate);

    /// <summary>
    /// An invoice can be cancelled only within its own month: after that it has gone into that month's GSTR-1, and only
    /// a credit note can correct it.
    /// </summary>
    public static bool InSameMonth(DateOnly documentDate, DateOnly today) => documentDate.Year == today.Year && documentDate.Month == today.Month;

    public static decimal RemainingQuantity(CreditableLine line, CreditedSoFar soFar) => line.Quantity - soFar.Quantity;

    public static decimal RemainingValue(CreditableLine line, CreditedSoFar soFar) => line.TaxableValue - soFar.TaxableValue;

    /// <summary>
    /// Credit for <paramref name="quantity"/> returned: at the line's value per unit still uncredited, so a price
    /// correction made earlier lowers what each returned unit is worth.
    /// </summary>
    public static CreditAmounts ForGoods(CreditableLine line, CreditedSoFar soFar, decimal quantity, bool isInterState, bool withGst)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(soFar);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        var remainingQuantity = RemainingQuantity(line, soFar);
        if (quantity > remainingQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "More than is left on the line.");
        }

        var remainingValue = RemainingValue(line, soFar);
        var taxable = quantity == remainingQuantity ? remainingValue : Money(remainingValue * quantity / remainingQuantity);
        return WithTax(line, soFar, taxable, taxable == remainingValue, isInterState, withGst);
    }

    /// <summary>Credit of <paramref name="taxableAmount"/> off the line's value, with no goods moving.</summary>
    public static CreditAmounts ForValue(CreditableLine line, CreditedSoFar soFar, decimal taxableAmount, bool isInterState, bool withGst)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(soFar);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(taxableAmount);
        var remainingValue = RemainingValue(line, soFar);
        if (taxableAmount > remainingValue)
        {
            throw new ArgumentOutOfRangeException(nameof(taxableAmount), "More than is left on the line.");
        }

        return WithTax(line, soFar, taxableAmount, taxableAmount == remainingValue, isInterState, withGst);
    }

    private static CreditAmounts WithTax(CreditableLine line, CreditedSoFar soFar, decimal taxable, bool isLast, bool isInterState, bool withGst)
    {
        if (!withGst)
        {
            return new CreditAmounts(taxable, 0m, 0m, 0m);
        }

        var cgstLeft = Math.Max(0m, line.CgstAmount - soFar.CgstAmount);
        var sgstLeft = Math.Max(0m, line.SgstAmount - soFar.SgstAmount);
        var igstLeft = Math.Max(0m, line.IgstAmount - soFar.IgstAmount);
        if (isLast)
        {
            return new CreditAmounts(taxable, cgstLeft, sgstLeft, igstLeft);
        }

        var tax = GstCalculator.CalculateLine(new GstLineInput(1m, taxable, 0m, line.GstRate), isInterState);
        return new CreditAmounts(taxable, Math.Min(tax.CgstAmount, cgstLeft), Math.Min(tax.SgstAmount, sgstLeft), Math.Min(tax.IgstAmount, igstLeft));
    }

    /// <summary>The document total rounded to the rupee, as invoices are, with the round-off.</summary>
    public static (decimal RoundOff, decimal GrandTotal) Total(IEnumerable<CreditAmounts> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var exact = lines.Sum(l => l.LineTotal);
        var total = Math.Round(exact, 0, MidpointRounding.AwayFromZero);
        return (total - exact, total);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
