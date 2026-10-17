using System.Globalization;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Auditing;

namespace SalesApp.Infrastructure.Notifications;

/// <summary>Amounts, quantities and payment modes as notifications show them.</summary>
internal static class NotificationText
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string Money(decimal amount) => AuditFormat.Money(amount);

    public static string Quantity(decimal quantity, string unit) => $"{quantity.ToString("#,##0.###", India)} {unit}";

    public static string Mode(PaymentMode mode) => mode switch
    {
        PaymentMode.Upi => "UPI",
        PaymentMode.Neft => "NEFT",
        PaymentMode.Rtgs => "RTGS",
        _ => mode.ToString(),
    };
}
