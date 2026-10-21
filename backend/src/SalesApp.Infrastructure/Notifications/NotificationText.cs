using System.Globalization;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Auditing;

namespace SalesApp.Infrastructure.Notifications;

/// <summary>
/// What each notification says, in one place for the real ones and the demo's. They're saved in English and read in
/// each viewer's language (NotificationService translates them with <see cref="L.T"/>), so every sentence is whole.
/// </summary>
internal static class NotificationText
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string Money(decimal amount) => AuditFormat.Money(amount);

    public static string Quantity(decimal quantity, ProductUnit unit) => $"{quantity.ToString("#,##0.###", India)} {unit}";

    public static string Mode(PaymentMode mode) => mode switch
    {
        PaymentMode.Upi => "UPI",
        PaymentMode.Neft => "NEFT",
        PaymentMode.Rtgs => "RTGS",
        PaymentMode.Cash => L.Msg("Cash"),
        PaymentMode.Cheque => L.Msg("Cheque"),
        _ => mode.ToString(),
    };

    public static (string Title, string Body) PaymentReceived(decimal amount, string customer, PaymentMode mode, string invoice) =>
        (L.Msg($"{Money(amount)} received from {customer}"), L.Msg($"{Mode(mode)} against {invoice}"));

    public static (string Title, string Body) OrderCreated(string order, string customer, int lines, decimal total) =>
        (L.Msg($"New order {order} for {customer}"), lines == 1 ? L.Msg($"1 line · {Money(total)}") : L.Msg($"{lines} lines · {Money(total)}"));

    public static (string Title, string Body) OrderConfirmed(string order, string customer, decimal total) =>
        (L.Msg($"Order {order} confirmed for {customer}"), L.Msg($"{Money(total)}, ready to invoice"));

    public static (string Title, string Body) PurchaseOrderApproval(string order, string supplier, decimal total, decimal? limit) =>
        (L.Msg($"Purchase order {order} needs your approval"),
            limit is { } above ? L.Msg($"{supplier} · {Money(total)}, above the {Money(above)} limit") : L.Msg($"{supplier} · {Money(total)}"));

    public static (string Title, string Body) BillApproval(string bill, string supplier, string? issue, decimal total) =>
        (L.Msg($"Bill {bill} from {supplier} needs approval"),
            issue is null ? L.Msg($"A line doesn't match its order. Bill total: {Money(total)}.") : L.Msg($"{issue} Bill total: {Money(total)}."));

    public static (string Title, string Body) LowStock(string product, decimal onHand, decimal reorderLevel, ProductUnit unit) =>
        (onHand <= 0 ? L.Msg($"{product} is out of stock") : L.Msg($"{product} is low on stock"),
            L.Msg($"{Quantity(onHand, unit)} left; reorder level {Quantity(reorderLevel, unit)}"));

    public static (string Title, string Body) OverdueSummary(int count, decimal total, string oldest, string customer, int daysLate)
    {
        var title = count == 1 ? L.Msg("1 invoice is overdue") : L.Msg(string.Create(India, $"{count} invoices are overdue"));
        var body = daysLate == 1
            ? L.Msg($"{Money(total)} in all. The oldest, {oldest} for {customer}, is 1 day late.")
            : L.Msg(string.Create(India, $"{Money(total)} in all. The oldest, {oldest} for {customer}, is {daysLate} days late."));
        return (title, body);
    }

    public static (string Title, string Body) InsightsReady() =>
        (L.Msg("New insights are ready"), L.Msg("Forecasts, unusual months and reorder suggestions have been worked out again."));
}
