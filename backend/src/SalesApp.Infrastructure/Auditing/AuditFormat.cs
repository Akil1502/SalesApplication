using System.Globalization;
using System.Text;
using SalesApp.Core.Entities;
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
        ["AssignedRepId"] = L.Msg("Sales rep"),
        ["CategoryId"] = L.Msg("Category"),
        ["HsnCodeId"] = L.Msg("HSN code"),
        ["CustomerId"] = L.Msg("Customer"),
        ["ProductId"] = L.Msg("Product"),
        ["InvoiceId"] = L.Msg("Invoice"),
        ["SalesOrderId"] = L.Msg("Order"),
        ["SupplierId"] = L.Msg("Supplier"),
        ["PreferredSupplierId"] = L.Msg("Preferred supplier"),
        ["PurchaseOrderId"] = L.Msg("Purchase order"),
        ["SupplierBillId"] = L.Msg("Bill"),
        ["ApprovedByUserId"] = L.Msg("Approved by"),
        ["SupplierInvoiceNumber"] = L.Msg("Supplier's invoice number"),
        ["PurchaseApprovalLimit"] = L.Msg("Purchase approval limit"),
        ["OverReceiptTolerancePercent"] = L.Msg("Over-receipt tolerance"),
        ["BillPriceTolerancePercent"] = L.Msg("Bill price tolerance"),
        ["ChargesGstRate"] = L.Msg("GST rate on charges"),
        ["BankIfsc"] = L.Msg("Bank IFSC"),
        ["Gstin"] = L.Msg("GSTIN"),
        ["Pan"] = L.Msg("PAN"),
        ["Ifsc"] = L.Msg("IFSC"),
        ["Sku"] = L.Msg("SKU"),
        ["IsActive"] = L.Msg("Active"),
        ["PaymentTermsDays"] = L.Msg("Payment terms (days)"),
        ["LeadTimeDays"] = L.Msg("Lead time (days)"),
        ["GstRate"] = L.Msg("GST rate"),
        ["PlaceOfSupplyStateCode"] = L.Msg("Place of supply"),
        ["StateCode"] = L.Msg("State"),
        ["Pincode"] = L.Msg("PIN code"),
        ["Line1"] = L.Msg("line 1"),
        ["Line2"] = L.Msg("line 2"),
        ["EffectiveFrom"] = L.Msg("From"),
        ["EffectiveTo"] = L.Msg("To"),
        ["Type"] = L.Msg("Type"),
        ["CreditNoteId"] = L.Msg("Credit note"),
        ["SupplierReturnId"] = L.Msg("Supplier return"),
        ["CreditNoteDate"] = L.Msg("Date"),
        ["ReturnDate"] = L.Msg("Date"),
        ["AdjustmentDate"] = L.Msg("Date"),
        ["RefundDate"] = L.Msg("Date"),
        ["Kind"] = L.Msg("Type"),
        ["DocumentLanguage"] = L.Msg("Document language"),
        ["ReasonNote"] = L.Msg("Details"),
        ["WithGst"] = L.Msg("Reduces GST"),
        ["AmountAdjusted"] = L.Msg("Set against invoices or bills"),
        ["AmountRefunded"] = L.Msg("Refunded"),
        ["AmountCredited"] = L.Msg("Credited"),
        ["SupplierCreditNoteNumber"] = L.Msg("Supplier's credit note number"),
        ["SupplierCreditNoteDate"] = L.Msg("Supplier's credit note date"),
    };

    /// <summary>Enum values that aren't plain words: payment modes are acronyms, sources say what AI read.</summary>
    private static readonly Dictionary<string, string> EnumWords = new(StringComparer.Ordinal)
    {
        ["Upi"] = L.Msg("UPI"),
        ["Neft"] = L.Msg("NEFT"),
        ["Rtgs"] = L.Msg("RTGS"),
        ["AiText"] = L.Msg("AI (text)"),
        ["AiPdf"] = L.Msg("AI (PDF)"),
        ["AiImage"] = L.Msg("AI (photo)"),
        ["GoodsReturn"] = L.Msg("Goods returned"),
        ["PriceCorrection"] = L.Msg("Price correction"),
        ["WrongItem"] = L.Msg("Wrong item"),
        ["QualityIssue"] = L.Msg("Quality issue"),
        ["ExcessSupplied"] = L.Msg("Excess supplied"),
        ["RateDifference"] = L.Msg("Rate difference"),
        ["ShortSupply"] = L.Msg("Short supply"),
        ["Restock"] = L.Msg("Back into stock"),
    };

    /// <summary>
    /// Every field name and value word the trail can write for the audited records, for the translation tests: labels
    /// are made from property names, so they aren't in the source as text.
    /// </summary>
    public static IEnumerable<string> TextsForTranslation()
    {
        static bool Scalar(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateOnly) || t == typeof(DateTime) || t == typeof(Guid);
        }

        var texts = new HashSet<string>(StringComparer.Ordinal) { Value(true, "")!, Value(false, "")! };
        foreach (var (type, spec) in AuditedTypes.Specs)
        {
            foreach (var property in type.GetProperties().Where(p => p.GetMethod?.IsPublic == true && !AuditedTypes.Ignored.Contains(p.Name)))
            {
                if (Scalar(property.PropertyType))
                {
                    texts.Add(Label(property.Name));
                    if ((Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) is { IsEnum: true } enumType)
                    {
                        foreach (var value in Enum.GetValues(enumType))
                        {
                            texts.Add(Value(value, property.Name)!);
                        }
                    }
                }
                else if (spec.IncludeOwned && property.PropertyType == typeof(Address))
                {
                    foreach (var part in typeof(Address).GetProperties())
                    {
                        texts.Add(Label(part.Name, property.Name));
                    }
                }
            }
        }

        return texts;
    }

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
        bool b => b ? L.Msg("Yes") : L.Msg("No"),
        DateOnly d => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        DateTime t => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), IndiaTime).ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal m => Decimal(m, property),
        double or float => Decimal(Convert.ToDecimal(value, CultureInfo.InvariantCulture), property),
        int or long or short => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", India),
        Enum e => EnumWords.TryGetValue(e.ToString(), out var words) ? words : Words(e.ToString()),
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
                return menu.Edit is null ? menu.Name : L.Msg($"{menu.Name}: view");
            }

            if (menu.Edit == key)
            {
                return L.Msg($"{menu.Name}: add & change");
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
