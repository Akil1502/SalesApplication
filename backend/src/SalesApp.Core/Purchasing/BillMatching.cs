using System.Globalization;
using SalesApp.Core.Tax;

namespace SalesApp.Core.Purchasing;

/// <summary>One bill line as the three-way check sees it: the bill, the order line, and what has arrived.</summary>
/// <param name="Quantity">Quantity on the bill.</param>
/// <param name="UnitPrice">Price per unit on the bill, before GST.</param>
/// <param name="GstRate">GST rate on the bill.</param>
/// <param name="OrderedPrice">The purchase order's price for the line.</param>
/// <param name="ProductGstRate">The rate for the product's HSN code today.</param>
/// <param name="Received">Everything received against the order line so far.</param>
/// <param name="BilledBefore">What other bills have already charged for on the order line.</param>
/// <param name="Unit">The product's unit, for the message.</param>
public sealed record BillLineCheck(
    decimal Quantity,
    decimal UnitPrice,
    decimal GstRate,
    decimal OrderedPrice,
    decimal ProductGstRate,
    decimal Received,
    decimal BilledBefore,
    string Unit);

/// <summary>
/// The three-way match: a bill should charge for no more than has been received, at the order's price (within the
/// company's tolerance), at the right GST rate, and add up to the total on the supplier's invoice. Each problem is
/// described in a sentence; a bill with any of them waits for approval before it can be paid.
/// </summary>
public static class BillMatching
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>A rupee either way is rounding, not a difference worth stopping a payment for.</summary>
    public const decimal TotalTolerance = 1m;

    public static string? LineIssue(BillLineCheck line, decimal priceTolerancePercent)
    {
        ArgumentNullException.ThrowIfNull(line);
        var issues = new List<string>();

        if (line.BilledBefore + line.Quantity > line.Received)
        {
            issues.Add(line.BilledBefore > 0
                ? $"Bills charge for {Quantity(line.BilledBefore + line.Quantity)} {line.Unit} in all, but only {Quantity(line.Received)} has been received."
                : $"Charges for {Quantity(line.Quantity)} {line.Unit}, but only {Quantity(line.Received)} has been received.");
        }

        if (line.OrderedPrice > 0)
        {
            var difference = (line.UnitPrice - line.OrderedPrice) / line.OrderedPrice * 100m;
            if (Math.Abs(difference) > priceTolerancePercent)
            {
                issues.Add($"Price ₹{Money(line.UnitPrice)} is {Math.Abs(Math.Round(difference, 1)).ToString("0.#", India)}% " +
                           $"{(difference > 0 ? "above" : "below")} the order's ₹{Money(line.OrderedPrice)}.");
            }
        }

        if (line.GstRate != line.ProductGstRate)
        {
            issues.Add($"GST at {Percent(line.GstRate)}, but the product's HSN rate is {Percent(line.ProductGstRate)}.");
        }

        return issues.Count == 0 ? null : string.Join(" ", issues);
    }

    /// <summary>A difference between our total and the one printed on the supplier's invoice, if any.</summary>
    public static string? TotalIssue(decimal grandTotal, decimal? supplierTotal) =>
        supplierTotal is { } stated && Math.Abs(stated - grandTotal) > TotalTolerance
            ? $"The bill works out to ₹{Money(grandTotal)}, but the supplier's invoice says ₹{Money(stated)}."
            : null;

    private static string Money(decimal amount) => AmountInWords.IndianGrouping(amount);

    private static string Quantity(decimal quantity) => quantity.ToString("#,##0.###", India);

    private static string Percent(decimal rate) => $"{rate.ToString("0.##", India)}%";
}
