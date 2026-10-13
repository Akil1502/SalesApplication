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

public sealed record DashboardKpisDto(
    decimal SalesMonthToDate,
    decimal SalesLastMonthSamePeriod,
    int InvoicesMonthToDate,
    decimal SalesFinancialYearToDate,
    decimal SalesPreviousYearSamePeriod,
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
public sealed record DashboardDto(
    string FinancialYear,
    DashboardKpisDto Kpis,
    IReadOnlyList<MonthlySalesDto> MonthlySales,
    IReadOnlyList<RankedItemDto> TopCustomers,
    IReadOnlyList<RankedItemDto> TopProducts,
    IReadOnlyList<RankedItemDto> SalesByState,
    ReceivablesAgeingDto Ageing);
