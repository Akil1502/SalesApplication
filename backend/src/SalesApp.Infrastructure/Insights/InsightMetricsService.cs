using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Insights;
using SalesApp.Core.Entities;
using SalesApp.Core.Forecasting;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Insights;

public interface IInsightMetricsService
{
    Task<InsightMetricsDto> ComputeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Computes every insight figure in C# from the sales data, with no AI involved: the sales overview, receivables,
/// a backtested monthly forecast, customer trends, reorder suggestions and anomalies. Amounts are whole rupees
/// and percentages have one decimal, so a written summary can be checked against them exactly.
/// </summary>
public sealed class InsightMetricsService(AppDbContext db, IBusinessClock clock) : IInsightMetricsService
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
    private const int ForecastMonths = 3;
    private const int HistoryMonthsShown = 24;
    private const int DemandWindowDays = 182;
    private const int MaxPerKind = 5;

    public async Task<InsightMetricsDto> ComputeAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var monthly = await MonthlySalesAsync(monthStart, cancellationToken);
        var seasonalIndex = monthly.Count >= 12 ? Statistics.MonthlySeasonalIndex([.. monthly.Select(m => (m.Month, (double)m.Sales))]) : Enumerable.Repeat(1.0, 12).ToArray();

        var sales = await SalesOverviewAsync(today, monthStart, cancellationToken);
        var receivables = await ReceivablesAsync(cancellationToken);
        var forecast = await ForecastAsync(monthly, monthStart, today, cancellationToken);
        var (top, growing, declining) = await CustomerTrendsAsync(today, cancellationToken);
        var reorder = await ReorderAsync(today, seasonalIndex, cancellationToken);

        var stockouts = await StockoutsAsync(today, cancellationToken);
        var anomalies = new List<AnomalyDto>();
        anomalies.AddRange(await StoppedBuyingAsync(today, cancellationToken));
        anomalies.AddRange(await UnusualInvoicesAsync(today, cancellationToken));
        anomalies.AddRange(stockouts.Take(MaxPerKind).Select(s => s.Anomaly));
        anomalies.AddRange(await DemandAnomaliesAsync(monthStart, seasonalIndex, stockouts, cancellationToken));
        anomalies.AddRange(await PaymentRisksAsync(cancellationToken));
        anomalies.AddRange(await HighReturnsAsync(today, cancellationToken));

        return new InsightMetricsDto(today, sales, receivables, forecast, top, growing, declining, reorder, anomalies);
    }

    /// <summary>Sales per full month, from the first full month with sales to last month; empty months are zero.</summary>
    private async Task<List<MonthSalesDto>> MonthlySalesAsync(DateOnly currentMonth, CancellationToken cancellationToken)
    {
        var rows = await db.RptSalesLines
            .Where(l => l.MonthStart < currentMonth)
            .GroupBy(l => l.MonthStart)
            .Select(g => new { Month = g.Key, Sales = g.Sum(l => l.TaxableValue), FirstDay = g.Min(l => l.InvoiceDate) })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        // A history that starts part-way through a month would make that month look like a slump.
        var first = rows.MinBy(r => r.Month)!;
        var start = first.FirstDay.Day <= 3 ? first.Month : first.Month.AddMonths(1);
        var byMonth = rows.ToDictionary(r => r.Month, r => r.Sales);
        var months = new List<MonthSalesDto>();
        for (var month = start; month < currentMonth; month = month.AddMonths(1))
        {
            months.Add(new MonthSalesDto(month, Math.Round(byMonth.GetValueOrDefault(month), 0)));
        }

        return months;
    }

    private async Task<SalesOverviewDto> SalesOverviewAsync(DateOnly today, DateOnly monthStart, CancellationToken cancellationToken)
    {
        var yearStart = FinancialYear.Start(today);
        var lastYearStart = yearStart.AddYears(-1);
        var lastYearToday = today.AddYears(-1);
        var lastMonth = monthStart.AddMonths(-1);

        var lines = db.RptSalesLines;
        var thisYear = await lines.Where(l => l.InvoiceDate >= yearStart && l.InvoiceDate <= today).SumAsync(l => l.TaxableValue, cancellationToken);
        var lastYear = await lines.Where(l => l.InvoiceDate >= lastYearStart && l.InvoiceDate <= lastYearToday).SumAsync(l => l.TaxableValue, cancellationToken);
        var lastMonthSales = await lines.Where(l => l.MonthStart == lastMonth).SumAsync(l => l.TaxableValue, cancellationToken);
        var sameMonthLastYear = await lines.Where(l => l.MonthStart == lastMonth.AddYears(-1)).SumAsync(l => l.TaxableValue, cancellationToken);
        var monthToDate = await lines.Where(l => l.InvoiceDate >= monthStart && l.InvoiceDate <= today).SumAsync(l => l.TaxableValue, cancellationToken);
        var invoices = db.RptInvoices.Where(i => i.InvoiceDate >= yearStart && i.InvoiceDate <= today);
        var invoiceCount = await invoices.CountAsync(cancellationToken);
        var customers = await invoices.Select(i => i.CustomerId).Distinct().CountAsync(cancellationToken);

        return new SalesOverviewDto(FinancialYear.Label(today), Rupees(thisYear), Rupees(lastYear), Growth(thisYear, lastYear),
            lastMonth, Rupees(lastMonthSales), Rupees(sameMonthLastYear), Growth(lastMonthSales, sameMonthLastYear), Rupees(monthToDate),
            invoiceCount, customers);
    }

    private async Task<ReceivablesOverviewDto> ReceivablesAsync(CancellationToken cancellationToken)
    {
        var open = await db.RptInvoices
            .Where(i => i.Outstanding > 0)
            .Select(i => new { i.CustomerId, i.Outstanding, i.DaysOverdue })
            .ToListAsync(cancellationToken);
        var overdue = open.Where(i => i.DaysOverdue > 0).ToList();
        return new ReceivablesOverviewDto(
            Rupees(open.Sum(i => i.Outstanding)),
            Rupees(overdue.Sum(i => i.Outstanding)),
            Rupees(overdue.Where(i => i.DaysOverdue > 90).Sum(i => i.Outstanding)),
            overdue.Count,
            overdue.Select(i => i.CustomerId).Distinct().Count());
    }

    private async Task<ForecastDto> ForecastAsync(List<MonthSalesDto> monthly, DateOnly monthStart, DateOnly today, CancellationToken cancellationToken)
    {
        var result = Forecaster.Forecast([.. monthly.Select(m => (double)m.Sales)], ForecastMonths);
        var byMonth = monthly.ToDictionary(m => m.Month, m => m.Sales);
        var monthToDate = await db.RptSalesLines.Where(l => l.InvoiceDate >= monthStart && l.InvoiceDate <= today).SumAsync(l => l.TaxableValue, cancellationToken);
        var next = result.Points
            .Select(p =>
            {
                var month = monthStart.AddMonths(p.Step - 1);
                return new ForecastMonthDto(month, Rupees((decimal)p.Value), Rupees((decimal)p.Low), Rupees((decimal)p.High),
                    byMonth.TryGetValue(month.AddYears(-1), out var lastYear) ? lastYear : null);
            })
            .ToList();
        return new ForecastDto(result.Method,
            [.. result.Backtests.Select(b => new BacktestDto(b.Method, Math.Round((decimal)b.MeanAbsolutePercentError, 1)))],
            result.HoldoutMonths, [.. monthly.TakeLast(HistoryMonthsShown)], next, Rupees(monthToDate));
    }

    private async Task<(List<CustomerTrendDto> Top, List<CustomerTrendDto> Growing, List<CustomerTrendDto> Declining)> CustomerTrendsAsync(
        DateOnly today, CancellationToken cancellationToken)
    {
        var yearStart = FinancialYear.Start(today);
        var lastYearStart = yearStart.AddYears(-1);
        var lastYearToday = today.AddYears(-1);
        var rows = await db.RptSalesLines
            .Where(l => l.InvoiceDate >= lastYearStart && l.InvoiceDate <= today)
            .GroupBy(l => l.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Name = g.Max(l => l.CustomerName),
                City = g.Max(l => l.CustomerCity),
                ThisYear = g.Sum(l => l.InvoiceDate >= yearStart ? l.TaxableValue : 0),
                LastYear = g.Sum(l => l.InvoiceDate <= lastYearToday ? l.TaxableValue : 0),
            })
            .ToListAsync(cancellationToken);

        var trends = rows
            .Select(r => new CustomerTrendDto(r.CustomerId, r.Name ?? "", r.City ?? "", Rupees(r.ThisYear), Rupees(r.LastYear), Rupees(r.ThisYear - r.LastYear), Growth(r.ThisYear, r.LastYear)))
            .ToList();
        return (
            [.. trends.Where(t => t.ThisYear > 0).OrderByDescending(t => t.ThisYear).Take(MaxPerKind)],
            [.. trends.Where(t => t.LastYearSamePeriod > 0 && t.Change > 0).OrderByDescending(t => t.Change).Take(MaxPerKind)],
            [.. trends.Where(t => t.LastYearSamePeriod > 0 && t.Change < 0).OrderBy(t => t.Change).Take(MaxPerKind)]);
    }

    /// <summary>
    /// Daily demand is measured over the days the product was in stock (a product that's out of stock sells
    /// nothing, which would otherwise look like no demand), adjusted for the season ahead.
    /// </summary>
    private async Task<List<ReorderSuggestionDto>> ReorderAsync(DateOnly today, double[] seasonalIndex, CancellationToken cancellationToken)
    {
        var from = today.AddDays(-DemandWindowDays + 1);
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Unit, p.ReorderLevel, p.ReorderQuantity, p.LeadTimeDays, OnHand = p.Stock != null ? p.Stock.QuantityOnHand : 0m })
            .ToListAsync(cancellationToken);
        var sold = (await db.RptSalesLines
                .Where(l => l.InvoiceDate >= from && l.InvoiceDate <= today)
                .GroupBy(l => new { l.ProductId, l.InvoiceDate })
                .Select(g => new { g.Key.ProductId, Day = g.Key.InvoiceDate, Quantity = g.Sum(l => l.Quantity) })
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.ProductId);
        var movements = await MovementsAsync(today, cancellationToken);

        // Demand over the coming lead time relative to the months the demand was measured in.
        var measuredSeason = Enumerable.Range(0, 6).Average(i => seasonalIndex[today.AddMonths(-i).Month - 1]);

        var suggestions = new List<ReorderSuggestionDto>();
        foreach (var product in products)
        {
            var daily = sold[product.Id].ToDictionary(s => s.Day, s => (double)s.Quantity);
            var balances = DailyBalances(movements[product.Id], from, today);
            var inStockDays = balances.Where(b => b.Opening > 0 || b.Closing > 0 || daily.ContainsKey(b.Day)).Select(b => b.Day).ToList();
            if (daily.Count == 0 || inStockDays.Count < 14)
            {
                continue;
            }

            var quantities = inStockDays.Select(d => daily.GetValueOrDefault(d)).ToList();
            var mean = quantities.Average();
            var sd = Math.Sqrt(quantities.Sum(q => (q - mean) * (q - mean)) / Math.Max(quantities.Count - 1, 1));
            var aheadSeason = Enumerable.Range(0, Math.Max(1, (product.LeadTimeDays + 29) / 30)).Average(i => seasonalIndex[today.AddMonths(i).Month - 1]);
            var demand = mean * (measuredSeason > 0 ? aheadSeason / measuredSeason : 1);

            var plan = Reorder.Plan(demand, sd, product.LeadTimeDays, (double)product.OnHand, (double)product.ReorderQuantity);
            if (!plan.ShouldReorder)
            {
                continue;
            }

            suggestions.Add(new ReorderSuggestionDto(product.Id, product.Sku, product.Name, product.Unit.ToString(), product.OnHand,
                Math.Round((decimal)plan.DailyDemand, 1), Math.Round((decimal)Math.Min(plan.DaysOfCover, 999), 1), product.LeadTimeDays,
                Math.Round((decimal)plan.ReorderPoint, 0), product.ReorderLevel, (decimal)plan.SuggestedQuantity, plan.RunsOutBeforeDelivery));
        }

        return [.. suggestions.OrderBy(s => s.DaysOfCover / Math.Max(s.LeadTimeDays, 1))];
    }

    private async Task<ILookup<int, (DateOnly Date, long Id, double Balance)>> MovementsAsync(DateOnly today, CancellationToken cancellationToken) =>
        (await db.StockMovements.AsNoTracking()
            .Where(m => m.MovementDate <= today)
            .Select(m => new { m.ProductId, m.MovementDate, m.Id, m.BalanceAfter })
            .ToListAsync(cancellationToken))
        .ToLookup(m => m.ProductId, m => (m.MovementDate, m.Id, (double)m.BalanceAfter));

    /// <summary>Stock at the start and end of each day in the window, rebuilt from the stock ledger.</summary>
    private static List<(DateOnly Day, double Opening, double Closing)> DailyBalances(IEnumerable<(DateOnly Date, long Id, double Balance)> movements, DateOnly from, DateOnly to)
    {
        var ordered = movements.OrderBy(m => m.Date).ThenBy(m => m.Id).ToList();
        var balance = ordered.LastOrDefault(m => m.Date < from).Balance;
        var endOfDay = ordered.Where(m => m.Date >= from).GroupBy(m => m.Date).ToDictionary(g => g.Key, g => g.Last().Balance);
        var days = new List<(DateOnly, double, double)>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var opening = balance;
            if (endOfDay.TryGetValue(day, out var closing))
            {
                balance = closing;
            }

            days.Add((day, opening, balance));
        }

        return days;
    }

    private sealed record Stockout(int ProductId, DateOnly From, DateOnly To, AnomalyDto Anomaly);

    /// <summary>
    /// Runs of a week or more in the last year when a product had no stock, from the stock ledger. The sales lost
    /// are estimated from the product's daily sales on the days it was in stock.
    /// </summary>
    private async Task<List<Stockout>> StockoutsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var from = today.AddDays(-364);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.Id, p.Sku, p.Name, p.Unit }).ToListAsync(cancellationToken);
        var movements = await MovementsAsync(today, cancellationToken);
        var sales = (await db.RptSalesLines
                .Where(l => l.InvoiceDate >= from && l.InvoiceDate <= today)
                .GroupBy(l => l.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(l => l.Quantity), Value = g.Sum(l => l.TaxableValue), Days = g.Select(l => l.InvoiceDate).Distinct().Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(s => s.ProductId);

        var found = new List<Stockout>();
        foreach (var product in products)
        {
            var balances = DailyBalances(movements[product.Id], from, today);
            var runs = new List<(DateOnly From, DateOnly To)>();
            DateOnly? start = null;
            foreach (var (day, _, closing) in balances)
            {
                if (closing <= 0)
                {
                    start ??= day;
                }
                else if (start is { } began)
                {
                    runs.Add((began, day.AddDays(-1)));
                    start = null;
                }
            }

            if (start is { } open)
            {
                runs.Add((open, today));
            }

            var longest = runs.Where(r => r.To.DayNumber - r.From.DayNumber + 1 >= 7).OrderByDescending(r => r.To.DayNumber - r.From.DayNumber).FirstOrDefault();
            if (longest == default || !sales.TryGetValue(product.Id, out var sold) || sold.Quantity <= 0)
            {
                continue;
            }

            var days = longest.To.DayNumber - longest.From.DayNumber + 1;
            var inStock = Math.Max(1, balances.Count(b => b.Closing > 0));
            var lostQuantity = Math.Round(sold.Quantity / inStock * days, 0);
            var lostValue = Rupees(sold.Value / sold.Quantity * lostQuantity);
            var ongoing = longest.To == today;
            var anomaly = new AnomalyDto(AnomalyKind.Stockout,
                ongoing ? L.Msg($"{product.Sku} has been out of stock for {days} days") : L.Msg($"{product.Sku} was out of stock for {days} days"),
                ongoing
                    ? L.Msg($"{product.Name}: out of stock since {Day(longest.From)}. At its usual rate that's about {Quantity(lostQuantity, product.Unit.ToString())} of sales missed so far (about {Money(lostValue)}).")
                    : L.Msg($"{product.Name}: from {Day(longest.From)} to {Day(longest.To)}. At its usual rate that's about {Quantity(lostQuantity, product.Unit.ToString())} of sales missed (about {Money(lostValue)})."),
                days / 7.0, longest.From, lostValue, lostQuantity, null, null, days, null, product.Id, null);
            found.Add(new Stockout(product.Id, longest.From, longest.To, anomaly));
        }

        return [.. found.OrderByDescending(s => s.Anomaly.Amount)];
    }

    /// <summary>Regular customers whose last invoice is far longer ago than their usual gap between orders.</summary>
    private async Task<List<AnomalyDto>> StoppedBuyingAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var invoices = (await db.RptInvoices
                .Where(i => i.InvoiceDate <= today)
                .Select(i => new { i.CustomerId, i.CustomerName, i.InvoiceDate, i.TaxableValue })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.CustomerId);

        var found = new List<AnomalyDto>();
        foreach (var customer in invoices)
        {
            var dates = customer.Select(i => i.InvoiceDate).Distinct().Order().ToList();
            if (dates.Count < 5)
            {
                continue;
            }

            var gaps = dates.Zip(dates.Skip(1), (a, b) => (double)(b.DayNumber - a.DayNumber)).ToList();
            var usualGap = Math.Max(1, Statistics.Median(gaps));
            var last = dates[^1];
            var since = today.DayNumber - last.DayNumber;
            if (since <= Math.Max(60, 3 * usualGap) || since > 730)
            {
                continue;
            }

            var name = customer.OrderBy(i => i.InvoiceDate).Last().CustomerName;
            var yearBefore = customer.Where(i => i.InvoiceDate > last.AddYears(-1)).Sum(i => i.TaxableValue);
            var gap = (int)Math.Round(usualGap);
            found.Add(new AnomalyDto(AnomalyKind.StoppedBuying,
                L.Msg($"{name} hasn't bought for {since} days"),
                L.Msg($"Usually orders every {gap} days; the last invoice was on {Day(last)}. Bought {Money(yearBefore)} in the year before that."),
                since / usualGap, last, Rupees(yearBefore), null, gap, null, since, customer.Key, null, null));
        }

        return [.. found.OrderByDescending(a => a.Amount).Take(MaxPerKind)];
    }

    /// <summary>Invoices in the last year far larger than the same customer's usual invoice.</summary>
    private async Task<List<AnomalyDto>> UnusualInvoicesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var invoices = (await db.RptInvoices
                .Select(i => new { i.CustomerId, i.CustomerName, i.InvoiceNumber, i.InvoiceDate, i.TaxableValue })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.CustomerId);

        var found = new List<AnomalyDto>();
        foreach (var customer in invoices)
        {
            var all = customer.Where(i => i.TaxableValue > 0).ToList();
            if (all.Count < 8)
            {
                continue;
            }

            foreach (var invoice in all.Where(i => i.InvoiceDate > today.AddYears(-1)))
            {
                // Compared with the customer's other invoices: at least four times the usual one, and at least twice
                // the size of their large ones (the 90th percentile), so ordinary lumpy orders aren't flagged.
                var others = all.Where(i => i.InvoiceNumber != invoice.InvoiceNumber).Select(i => (double)i.TaxableValue).Order().ToList();
                var usual = (decimal)Statistics.Median(others);
                var large = (decimal)others[(int)Math.Floor(0.9 * (others.Count - 1))];
                var ratio = usual > 0 ? invoice.TaxableValue / usual : 0;
                var z = large > 0 ? (double)(invoice.TaxableValue / large) : 0;
                if (ratio >= 4 && invoice.TaxableValue >= 2 * large)
                {
                    found.Add(new AnomalyDto(AnomalyKind.UnusualInvoice,
                        L.Msg($"{invoice.InvoiceNumber} for {invoice.CustomerName} was {Math.Round(ratio, 1).ToString(India)} times their usual invoice"),
                        L.Msg($"{Money(invoice.TaxableValue)} on {Day(invoice.InvoiceDate)}, against a usual {Money(usual)} (excluding GST)."),
                        z, invoice.InvoiceDate, Rupees(invoice.TaxableValue), null, Rupees(usual), Math.Round(ratio, 1), null,
                        customer.Key, null, invoice.InvoiceNumber));
                }
            }
        }

        return [.. found.OrderByDescending(a => a.Score).Take(MaxPerKind)];
    }

    /// <summary>
    /// Months in the last year when a product sold far more or far less than usual for the time of year.
    /// Quantities are divided by the company's seasonal index before comparing, so October's festive rush
    /// isn't flagged as a spike. A month a stock-out explains isn't reported again as a drop.
    /// </summary>
    private async Task<List<AnomalyDto>> DemandAnomaliesAsync(DateOnly currentMonth, double[] seasonalIndex, List<Stockout> stockouts, CancellationToken cancellationToken)
    {
        var from = currentMonth.AddMonths(-24);
        var rows = await db.RptSalesLines
            .Where(l => l.MonthStart >= from && l.MonthStart < currentMonth)
            .GroupBy(l => new { l.ProductId, l.Sku, l.ProductName, l.Unit, l.MonthStart })
            .Select(g => new { g.Key.ProductId, g.Key.Sku, g.Key.ProductName, g.Key.Unit, g.Key.MonthStart, Quantity = g.Sum(l => l.Quantity) })
            .ToListAsync(cancellationToken);
        var firstMonth = rows.Count == 0 ? currentMonth : rows.Min(r => r.MonthStart);

        var spikes = new List<AnomalyDto>();
        var drops = new List<AnomalyDto>();
        foreach (var product in rows.GroupBy(r => r.ProductId))
        {
            var sample = product.First();
            var byMonth = product.ToDictionary(r => r.MonthStart, r => (double)r.Quantity);
            var months = new List<(DateOnly Month, double Raw, double Adjusted)>();
            for (var month = firstMonth; month < currentMonth; month = month.AddMonths(1))
            {
                var raw = byMonth.GetValueOrDefault(month);
                months.Add((month, raw, raw / seasonalIndex[month.Month - 1]));
            }

            if (months.Count(m => m.Raw > 0) < 6)
            {
                continue;
            }

            AnomalyDto? spike = null;
            AnomalyDto? drop = null;
            foreach (var point in months.Where(m => m.Month >= currentMonth.AddMonths(-12)))
            {
                var others = months.Where(m => m.Month != point.Month).Select(m => m.Adjusted).ToList();
                var z = Statistics.RobustZ(point.Adjusted, others);
                var typicalAdjusted = Statistics.Median(others);
                var typical = (decimal)Math.Round(typicalAdjusted * seasonalIndex[point.Month.Month - 1], 0);
                var ratio = typicalAdjusted > 0 ? point.Adjusted / typicalAdjusted : 0;
                var label = point.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                var quantity = Math.Round((decimal)point.Raw, 0);

                if (z >= 3.5 && ratio >= 2 && (spike is null || z > spike.Score))
                {
                    spike = new AnomalyDto(AnomalyKind.DemandSpike,
                        L.Msg($"{sample.Sku} sold {Math.Round(ratio, 1).ToString(India)} times its usual amount in {label}"),
                        L.Msg($"{sample.ProductName}: {Quantity(quantity, sample.Unit)} against a usual {Quantity(typical, sample.Unit)} for that time of year."),
                        z, point.Month, null, quantity, typical, Math.Round((decimal)ratio, 1), null, null, product.Key, null);
                }
                else if (typicalAdjusted > 0 && ratio <= 0.25 && (drop is null || 1 - ratio > drop.Score)
                    && !stockouts.Any(o => o.ProductId == product.Key && o.From < point.Month.AddMonths(1) && o.To >= point.Month))
                {
                    drop = new AnomalyDto(AnomalyKind.DemandDrop,
                        L.Msg($"{sample.Sku} nearly stopped selling in {label}"),
                        L.Msg($"{sample.ProductName}: {Quantity(quantity, sample.Unit)} against a usual {Quantity(typical, sample.Unit)}, though it was in stock."),
                        1 - ratio, point.Month, null, quantity, typical, Math.Round((decimal)ratio, 2), null, null, product.Key, null);
                }
            }

            if (spike is not null)
            {
                spikes.Add(spike);
            }

            if (drop is not null)
            {
                drops.Add(drop);
            }
        }

        return [.. spikes.OrderByDescending(a => a.Score).Take(MaxPerKind), .. drops.OrderByDescending(a => a.Score).Take(MaxPerKind)];
    }

    /// <summary>Customers with ₹10,000 or more over 60 days past due, the largest first.</summary>
    private async Task<List<AnomalyDto>> PaymentRisksAsync(CancellationToken cancellationToken)
    {
        var overdue = await db.RptInvoices
            .Where(i => i.Outstanding > 0 && i.DaysOverdue > 0)
            .GroupBy(i => i.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Name = g.Max(i => i.CustomerName),
                Overdue = g.Sum(i => i.Outstanding),
                Late = g.Sum(i => i.DaysOverdue > 60 ? i.Outstanding : 0),
                Oldest = g.Max(i => i.DaysOverdue),
            })
            .ToListAsync(cancellationToken);
        var ids = overdue.Where(o => o.Late >= 10_000).Select(o => o.CustomerId).ToList();
        var lastPaid = await db.RptPayments
            .Where(p => ids.Contains(p.CustomerId))
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, Last = g.Max(p => p.PaymentDate) })
            .ToDictionaryAsync(p => p.CustomerId, p => p.Last, cancellationToken);

        return [.. overdue
            .Where(o => o.Late >= 10_000)
            .OrderByDescending(o => o.Late)
            .Take(MaxPerKind)
            .Select(o => new AnomalyDto(AnomalyKind.PaymentRisk,
                L.Msg($"{o.Name}: {Money(o.Late)} more than 60 days overdue"),
                string.Join(' ',
                    lastPaid.TryGetValue(o.CustomerId, out var last) ? L.Msg($"Last payment on {Day(last)}.") : L.Msg("No payment on record."),
                    L.Msg($"The oldest overdue invoice is {o.Oldest} days past due; {Money(o.Overdue)} is overdue in all.")),
                (double)(o.Late / 100_000), lastPaid.TryGetValue(o.CustomerId, out var paid) ? paid : null,
                Rupees(o.Late), null, null, null, o.Oldest, o.CustomerId, null, null))];
    }

    /// <summary>
    /// Products and customers whose goods come back far more often than the rest: at least 5% returned, three times the
    /// usual rate, and on several credit notes, over the last 12 months. Price corrections don't count as returns.
    /// </summary>
    private async Task<List<AnomalyDto>> HighReturnsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var from = today.AddDays(-365);
        var lines = db.RptSalesLines.Where(l => l.InvoiceDate > from && l.InvoiceDate <= today);

        var products = await lines
            .GroupBy(l => new { l.ProductId, l.Sku, l.ProductName, l.Unit })
            .Select(g => new
            {
                g.Key.ProductId, g.Key.Sku, g.Key.ProductName, g.Key.Unit,
                Sold = g.Sum(l => l.DocumentType == "Invoice" ? l.Quantity : 0m),
                Returned = g.Sum(l => l.DocumentType == "CreditNote" && l.Quantity < 0 ? -l.Quantity : 0m),
                Notes = g.Where(l => l.DocumentType == "CreditNote" && l.Quantity < 0).Select(l => l.DocumentNumber).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        var customers = await lines
            .GroupBy(l => new { l.CustomerId, l.CustomerName })
            .Select(g => new
            {
                g.Key.CustomerId, g.Key.CustomerName,
                Bought = g.Sum(l => l.DocumentType == "Invoice" ? l.TaxableValue : 0m),
                Returned = g.Sum(l => l.DocumentType == "CreditNote" && l.Quantity < 0 ? -l.TaxableValue : 0m),
                Notes = g.Where(l => l.DocumentType == "CreditNote" && l.Quantity < 0).Select(l => l.DocumentNumber).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        static decimal Percent(decimal part, decimal whole) => whole > 0 ? Math.Round(part / whole * 100m, 1) : 0m;
        static bool Unusual(decimal rate, decimal typical) => rate >= 5m && rate >= 3m * typical;

        var soldProducts = products.Where(p => p.Sold > 0).ToList();
        var typicalProduct = soldProducts.Count == 0 ? 0m : Math.Round((decimal)Statistics.Median([.. soldProducts.Select(p => (double)Percent(p.Returned, p.Sold))]), 1);
        var productAlerts = soldProducts
            .Select(p => (p, Rate: Percent(p.Returned, p.Sold)))
            .Where(x => x.p.Notes >= 3 && Unusual(x.Rate, typicalProduct))
            .OrderByDescending(x => x.Rate)
            .Take(MaxPerKind)
            .Select(x => new AnomalyDto(AnomalyKind.HighReturns,
                L.Msg($"{x.p.Sku}: {x.Rate.ToString("0.#", India)}% of what was sold came back"),
                string.Join(' ',
                    L.Msg($"{x.p.ProductName}: {Quantity(Math.Round(x.p.Returned, 0), x.p.Unit)} returned out of {Quantity(Math.Round(x.p.Sold, 0), x.p.Unit)} sold in the last 12 months, on {x.p.Notes} credit notes."),
                    typicalProduct > 0 ? L.Msg($"Across products the usual rate is {typicalProduct.ToString("0.#", India)}%.") : L.Msg("Most products had no returns at all.")),
                (double)(x.Rate / Math.Max(typicalProduct, 0.5m)), null, null, Math.Round(x.p.Returned, 0), typicalProduct, x.Rate, null, null, x.p.ProductId, null));

        var buyers = customers.Where(c => c.Bought > 0).ToList();
        var typicalCustomer = buyers.Count == 0 ? 0m : Math.Round((decimal)Statistics.Median([.. buyers.Select(c => (double)Percent(c.Returned, c.Bought))]), 1);
        var customerAlerts = buyers
            .Select(c => (c, Rate: Percent(c.Returned, c.Bought)))
            .Where(x => x.c.Notes >= 2 && x.c.Returned >= 10_000 && Unusual(x.Rate, typicalCustomer))
            .OrderByDescending(x => x.c.Returned)
            .Take(MaxPerKind)
            .Select(x => new AnomalyDto(AnomalyKind.HighReturns,
                L.Msg($"{x.c.CustomerName} returned {x.Rate.ToString("0.#", India)}% of what they bought"),
                string.Join(' ',
                    L.Msg($"{Money(x.c.Returned)} of goods came back out of {Money(x.c.Bought)} bought in the last 12 months, on {x.c.Notes} credit notes."),
                    typicalCustomer > 0 ? L.Msg($"Customers usually return {typicalCustomer.ToString("0.#", India)}%.") : L.Msg("Most customers returned nothing.")),
                (double)(x.Rate / Math.Max(typicalCustomer, 0.5m)), null, Rupees(x.c.Returned), null, typicalCustomer, x.Rate, null, x.c.CustomerId, null, null));

        return [.. productAlerts, .. customerAlerts];
    }

    private static decimal Rupees(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal? Growth(decimal current, decimal previous) =>
        previous > 0 ? Math.Round((current / previous - 1) * 100, 1, MidpointRounding.AwayFromZero) : null;

    private static string Money(decimal value) => "₹" + Rupees(value).ToString("#,##0", India);

    private static string Quantity(decimal value, string unit) => $"{value.ToString("#,##0", India)} {unit}";

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
