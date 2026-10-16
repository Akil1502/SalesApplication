using SalesApp.Core.Entities;
using SalesApp.Core.Forecasting;

namespace SalesApp.Core.Contracts.Insights;

/// <summary>
/// The latest insights: figures computed in C# from the sales data (forecast, trends, reorder suggestions,
/// anomalies), and Claude's written summary of them, kept only where every figure it quotes is in the metrics.
/// </summary>
public sealed record InsightsDto(
    long RunId,
    DateTime GeneratedAtUtc,
    InsightTrigger Trigger,
    InsightMetricsDto Metrics,
    InsightNarrativeDto? Narrative,
    NarrativeStatus NarrativeStatus,
    string? NarrativeNote,
    decimal CostInr);

public sealed record InsightMetricsDto(
    DateOnly AsOf,
    SalesOverviewDto Sales,
    ReceivablesOverviewDto Receivables,
    ForecastDto Forecast,
    IReadOnlyList<CustomerTrendDto> TopCustomers,
    IReadOnlyList<CustomerTrendDto> Growing,
    IReadOnlyList<CustomerTrendDto> Declining,
    IReadOnlyList<ReorderSuggestionDto> Reorder,
    IReadOnlyList<AnomalyDto> Anomalies);

/// <summary>Sales excluding GST. "Same period last year" stops at the same day of the year as today.</summary>
public sealed record SalesOverviewDto(
    string FinancialYear,
    decimal FinancialYearToDate,
    decimal SamePeriodLastYear,
    decimal? GrowthPercent,
    DateOnly LastFullMonth,
    decimal LastFullMonthSales,
    decimal SameMonthLastYear,
    decimal? MonthGrowthPercent,
    decimal MonthToDate,
    int InvoicesThisYear,
    int ActiveCustomersThisYear);

public sealed record ReceivablesOverviewDto(
    decimal Outstanding,
    decimal Overdue,
    decimal OverdueOver90Days,
    int OverdueInvoices,
    int CustomersOverdue);

public sealed record BacktestDto(ForecastMethod Method, decimal MeanAbsolutePercentError);

public sealed record MonthSalesDto(DateOnly Month, decimal Sales);

public sealed record ForecastMonthDto(DateOnly Month, decimal Forecast, decimal Low, decimal High, decimal? SameMonthLastYear);

/// <summary>
/// Monthly sales forecast. <see cref="History"/> holds full months only; the current month is forecast whole and
/// <see cref="MonthToDate"/> shows how far it has got.
/// </summary>
public sealed record ForecastDto(
    ForecastMethod Method,
    IReadOnlyList<BacktestDto> Backtests,
    int HoldoutMonths,
    IReadOnlyList<MonthSalesDto> History,
    IReadOnlyList<ForecastMonthDto> Next,
    decimal MonthToDate);

/// <summary>A customer's sales this financial year to date against the same period last year.</summary>
public sealed record CustomerTrendDto(int CustomerId, string Name, string City, decimal ThisYear, decimal LastYearSamePeriod, decimal Change, decimal? ChangePercent);

/// <summary>A product at or below its suggested reorder point, with the quantity to order.</summary>
public sealed record ReorderSuggestionDto(
    int ProductId,
    string Sku,
    string Name,
    string Unit,
    decimal OnHand,
    decimal DailyDemand,
    decimal DaysOfCover,
    int LeadTimeDays,
    decimal ReorderPoint,
    decimal ConfiguredReorderLevel,
    decimal SuggestedQuantity,
    bool RunsOutBeforeDelivery);

public enum AnomalyKind
{
    /// <summary>A regular customer hasn't bought for much longer than usual.</summary>
    StoppedBuying,

    /// <summary>An invoice far larger than the customer's usual ones.</summary>
    UnusualInvoice,

    /// <summary>A product sold far more than usual in a month, allowing for the season.</summary>
    DemandSpike,

    /// <summary>A product's sales nearly stopped for a month, with no stock-out to explain it.</summary>
    DemandDrop,

    /// <summary>A product was out of stock for a week or more (from the stock ledger), with the sales likely lost.</summary>
    Stockout,

    /// <summary>A customer with a significant amount more than 60 days past due.</summary>
    PaymentRisk,

    /// <summary>
    /// A product or customer with far more goods coming back on credit notes than usual over the last 12 months.
    /// Ratio is its return rate in percent; Typical is the usual rate across products or customers, in percent.
    /// </summary>
    HighReturns,
}

/// <summary>
/// Something out of the ordinary. The figures are given as numbers (<see cref="Amount"/> in rupees,
/// <see cref="Quantity"/> in the product's unit, <see cref="Typical"/> in the same terms, <see cref="Ratio"/>,
/// <see cref="Days"/>) as well as in the sentence, so a summary quoting them can be checked.
/// </summary>
public sealed record AnomalyDto(
    AnomalyKind Kind,
    string Title,
    string Detail,
    double Score,
    DateOnly? Date,
    decimal? Amount,
    decimal? Quantity,
    decimal? Typical,
    decimal? Ratio,
    int? Days,
    int? CustomerId,
    int? ProductId,
    string? Reference);

public enum InsightCardKind
{
    Opportunity,
    Risk,
    Action,
    Info,
}

public sealed record InsightCardDto(InsightCardKind Kind, string Title, string Body);

public sealed record InsightNarrativeDto(string Headline, string Summary, IReadOnlyList<InsightCardDto> Cards);
