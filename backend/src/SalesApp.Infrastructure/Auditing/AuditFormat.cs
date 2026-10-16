using System.Globalization;
using System.Text;
using SalesApp.Core.Security;

namespace SalesApp.Infrastructure.Auditing;

/// <summary>
/// Turns property names and values into what the audit trail shows: "CreditLimit" becomes "Credit limit" and
/// 750000m becomes "₹7,50,000". Values are written when the change happens, so the trail reads the same later
/// even if, say, a sales rep is renamed.
/// </summary>
public static class AuditFormat
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
    private static readonly TimeZoneInfo IndiaTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["AssignedRepId"] = "Sales rep",
        ["CategoryId"] = "Category",
        ["HsnCodeId"] = "HSN code",
        ["CustomerId"] = "Customer",
        ["ProductId"] = "Product",
        ["InvoiceId"] = "Invoice",
        ["SalesOrderId"] = "Order",
        ["SupplierId"] = "Supplier",
        ["PreferredSupplierId"] = "Preferred supplier",
        ["PurchaseOrderId"] = "Purchase order",
        ["SupplierBillId"] = "Bill",
        ["ApprovedByUserId"] = "Approved by",
        ["SupplierInvoiceNumber"] = "Supplier's invoice number",
        ["PurchaseApprovalLimit"] = "Purchase approval limit",
        ["OverReceiptTolerancePercent"] = "Over-receipt tolerance",
        ["BillPriceTolerancePercent"] = "Bill price tolerance",
        ["ChargesGstRate"] = "GST rate on charges",
        ["BankIfsc"] = "Bank IFSC",
        ["Gstin"] = "GSTIN",
        ["Pan"] = "PAN",
        ["Ifsc"] = "IFSC",
        ["Sku"] = "SKU",
        ["IsActive"] = "Active",
        ["PaymentTermsDays"] = "Payment terms (days)",
        ["LeadTimeDays"] = "Lead time (days)",
        ["GstRate"] = "GST rate",
        ["PlaceOfSupplyStateCode"] = "Place of supply",
        ["StateCode"] = "State",
        ["Pincode"] = "PIN code",
        ["Line1"] = "line 1",
        ["Line2"] = "line 2",
        ["EffectiveFrom"] = "From",
        ["EffectiveTo"] = "To",
        ["Type"] = "Type",
    };

    /// <summary>"CreditLimit" → "Credit limit", "BillingAddress" + "City" → "Billing address: city".</summary>
    public static string Label(string property, string? ownerNavigation = null)
    {
        var bare = property.EndsWith("Utc", StringComparison.Ordinal) ? property[..^3]
            : property.EndsWith("Id", StringComparison.Ordinal) && property.Length > 2 ? property[..^2]
            : property;
        var own = Labels.TryGetValue(property, out var known) ? known : Words(bare);
        // Inside a sentence: "Billing address: city", but acronyms stay as they are ("Billing address: PIN code").
        var inner = own.Length > 1 && char.IsUpper(own[1]) ? own : char.ToLowerInvariant(own[0]) + own[1..];
        return ownerNavigation is null ? own : $"{Words(ownerNavigation)}: {inner}";
    }

    /// <summary>A value as people read it; null when empty. The property name decides money, quantity or percent.</summary>
    public static string? Value(object? value, string property) => value switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : (s.Length > 500 ? s[..497] + "…" : s),
        bool b => b ? "Yes" : "No",
        DateOnly d => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        DateTime t => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), IndiaTime).ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal m => Decimal(m, property),
        double or float => Decimal(Convert.ToDecimal(value, CultureInfo.InvariantCulture), property),
        int or long or short => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", India),
        Enum e => Words(e.ToString()),
        Guid g => g.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    /// <summary>A permission key as the permission list names it, e.g. "Invoices: add &amp; change" or "See cost prices and margins".</summary>
    public static string Permission(string key)
    {
        foreach (var menu in Permissions.Menus)
        {
            if (menu.View == key)
            {
                return menu.Edit is null ? menu.Name : $"{menu.Name}: view";
            }

            if (menu.Edit == key)
            {
                return $"{menu.Name}: add & change";
            }
        }

        return Permissions.SpecialRights.FirstOrDefault(r => r.Key == key)?.Name ?? key;
    }

    public static string Money(decimal amount) => "₹" + Grouped(amount, 2);

    private static string? Decimal(decimal value, string property)
    {
        if (property.EndsWith("Percent", StringComparison.Ordinal) || property.EndsWith("GstRate", StringComparison.Ordinal))
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        if (property.Contains("Quantity", StringComparison.Ordinal) || property.EndsWith("Level", StringComparison.Ordinal)
            || property.Contains("OnHand", StringComparison.Ordinal) || property == "BalanceAfter")
        {
            return Grouped(value, 3);
        }

        return Money(value);
    }

    /// <summary>Indian digit grouping (12,34,567), with decimals only when there are some.</summary>
    private static string Grouped(decimal value, int maxDecimals)
    {
        var rounded = decimal.Round(value, maxDecimals);
        var format = rounded == decimal.Truncate(rounded) ? "#,##,##0" : "#,##,##0." + new string('#', maxDecimals);
        return rounded.ToString(format, India);
    }

    /// <summary>"PartiallyPaid" → "Partially paid".</summary>
    private static string Words(string name)
    {
        var text = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                text.Append(' ');
                text.Append(char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsUpper(name[i + 1])) ? c : char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }
}
