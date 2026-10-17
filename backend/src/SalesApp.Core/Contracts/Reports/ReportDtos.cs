namespace SalesApp.Core.Contracts.Reports;

public enum ReportColumnType
{
    Text,
    Count,
    Money,
    Quantity,
    Percent,
    Date,
}

public sealed record ReportColumn(string Key, string Header, ReportColumnType Type);

public sealed record ReportDefinitionDto(string Key, string Title, string Description, bool UsesDateRange);

/// <summary>Query string for a report. Without dates, a report covers the current financial year to date.</summary>
public sealed record ReportRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>For sales by period: "month" (default) or "day".</summary>
    public string? Period { get; init; }
}

/// <summary>
/// A tabular report: typed columns, rows keyed by column key, and a totals row. The same result feeds the
/// on-screen table and the Excel export. Percent values are fractions (0.25 = 25%).
/// </summary>
public sealed record ReportResult(
    string Key,
    string Title,
    string Subtitle,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    Dictionary<string, object?> Totals);

/// <summary>The span the dashboard shows. Point-in-time figures (receivables, orders to invoice, stock) ignore it.</summary>
public enum DashboardPeriod
{
    Month,
    Quarter,
    Year,
    LastYear,
}

/// <summary>The days a dashboard covers and the earlier days its sales are compared with.</summary>
public sealed record DashboardPeriodDto(DashboardPeriod Period, DateOnly From, DateOnly To, DateOnly ComparisonFrom, DateOnly ComparisonTo);

/// <param name="Sales">Taxable value invoiced in the period, less credit notes.</param>
/// <param name="SalesComparison">The same for the comparison days.</param>
/// <param name="Invoices">Invoices issued in the period.</param>
/// <param name="Collected">Payments received in the period.</param>
/// <param name="Payments">How many payments that was.</param>
/// <param name="Outstanding">Owed by customers today.</param>
/// <param name="Overdue">The part of it past its due date.</param>
/// <param name="OverdueInvoices">How many invoices that is.</param>
/// <param name="OpenOrdersValue">Confirmed orders waiting to be invoiced, with GST.</param>
/// <param name="OpenOrders">How many orders that is.</param>
/// <param name="LowStockProducts">Active products at or below their reorder level.</param>
public sealed record DashboardKpisDto(
    decimal Sales,
    decimal SalesComparison,
    int Invoices,
    decimal Collected,
    int Payments,
    decimal Outstanding,
    decimal Overdue,
    int OverdueInvoices,
    decimal OpenOrdersValue,
    int OpenOrders,
    int LowStockProducts);

public sealed record MonthlySalesDto(DateOnly Month, decimal TaxableValue, int Invoices);

public sealed record RankedItemDto(string Label, decimal Value, decimal Share, string? Detail);

public sealed record ReceivablesAgeingDto(decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90);

/// <summary>Everything the home dashboard shows. Sales values are taxable value (excluding GST).</summary>
/// <param name="FinancialYear">The financial year the period falls in, e.g. "26-27".</param>
/// <param name="Period">The days shown and the days they're compared with.</param>
/// <param name="Kpis">The headline figures.</param>
/// <param name="TopCustomers">The period's top five customers.</param>
/// <param name="TopProducts">The period's top five products.</param>
/// <param name="SalesByState">The period's sales by place of supply.</param>
/// <param name="MonthlySales">The last 24 months, whatever the period.</param>
/// <param name="Ageing">Today's receivables by how late they are.</param>
public sealed record DashboardDto(
    string FinancialYear,
    DashboardPeriodDto Period,
    DashboardKpisDto Kpis,
    IReadOnlyList<MonthlySalesDto> MonthlySales,
    IReadOnlyList<RankedItemDto> TopCustomers,
    IReadOnlyList<RankedItemDto> TopProducts,
    IReadOnlyList<RankedItemDto> SalesByState,
    ReceivablesAgeingDto Ageing);
