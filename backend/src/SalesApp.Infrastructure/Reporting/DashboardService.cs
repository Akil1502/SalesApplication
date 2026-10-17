using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Reporting;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(DashboardPeriod period, CancellationToken cancellationToken);
}

/// <summary>Home dashboard figures, from the same rpt.* views as the reports, so the numbers always agree.</summary>
public sealed class DashboardService(AppDbContext db, IBusinessClock clock) : IDashboardService
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public async Task<DashboardDto> GetAsync(DashboardPeriod period, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var window = DashboardPeriods.Window(period, today);
        var trendStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-23);

        var lines = db.RptSalesLines;

        var sales = await lines.Where(l => l.InvoiceDate >= window.From && l.InvoiceDate <= window.To).SumAsync(l => (decimal?)l.TaxableValue, cancellationToken) ?? 0m;
        var comparison = await lines.Where(l => l.InvoiceDate >= window.ComparisonFrom && l.InvoiceDate <= window.ComparisonTo).SumAsync(l => (decimal?)l.TaxableValue, cancellationToken) ?? 0m;
        var invoiceCount = await db.RptInvoices.CountAsync(i => i.InvoiceDate >= window.From && i.InvoiceDate <= window.To, cancellationToken);
        var received = db.RptPayments.Where(p => p.PaymentDate >= window.From && p.PaymentDate <= window.To);
        var collected = await received.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var paymentCount = await received.CountAsync(cancellationToken);

        var open = db.RptInvoices.Where(i => i.Outstanding > 0);
        var outstanding = await open.SumAsync(i => (decimal?)i.Outstanding, cancellationToken) ?? 0m;
        var overdue = await open.Where(i => i.DaysOverdue > 0).SumAsync(i => (decimal?)i.Outstanding, cancellationToken) ?? 0m;
        var overdueCount = await open.CountAsync(i => i.DaysOverdue > 0, cancellationToken);

        var openOrders = db.SalesOrders.Where(o => o.Status == SalesOrderStatus.Confirmed);
        var openOrdersValue = await openOrders.SumAsync(o => (decimal?)o.GrandTotal, cancellationToken) ?? 0m;
        var openOrderCount = await openOrders.CountAsync(cancellationToken);
        var lowStock = await db.RptProducts.CountAsync(p => p.IsActive && p.StockStatus != "InStock", cancellationToken);

        var monthly = await lines.Where(l => l.InvoiceDate >= trendStart && l.InvoiceDate <= today)
            .GroupBy(l => l.MonthStart)
            .Select(g => new { Month = g.Key, Taxable = g.Sum(l => l.TaxableValue), Invoices = g.Where(l => l.DocumentType == "Invoice").Select(l => l.InvoiceId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var trend = Enumerable.Range(0, 24)
            .Select(i => trendStart.AddMonths(i))
            .Select(m => monthly.FirstOrDefault(x => x.Month == m) is { } x ? new MonthlySalesDto(m, x.Taxable, x.Invoices) : new MonthlySalesDto(m, 0m, 0))
            .ToList();

        var periodLines = lines.Where(l => l.InvoiceDate >= window.From && l.InvoiceDate <= window.To);
        var topCustomers = await periodLines.GroupBy(l => new { l.CustomerId, l.CustomerName, l.CustomerCity })
            .Select(g => new { g.Key.CustomerName, g.Key.CustomerCity, Value = g.Sum(l => l.TaxableValue) })
            .OrderByDescending(x => x.Value).Take(5).ToListAsync(cancellationToken);
        var topProducts = await periodLines.GroupBy(l => new { l.ProductId, l.ProductName, l.Unit })
            .Select(g => new { g.Key.ProductName, g.Key.Unit, Value = g.Sum(l => l.TaxableValue), Quantity = g.Sum(l => l.Quantity) })
            .OrderByDescending(x => x.Value).Take(5).ToListAsync(cancellationToken);
        var byState = await periodLines.GroupBy(l => l.StateName)
            .Select(g => new { State = g.Key, Value = g.Sum(l => l.TaxableValue) })
            .OrderByDescending(x => x.Value).ToListAsync(cancellationToken);

        var ageing = await open.GroupBy(_ => 1).Select(g => new ReceivablesAgeingDto(
                g.Sum(i => i.DaysOverdue == 0 ? i.Outstanding : 0m),
                g.Sum(i => i.DaysOverdue >= 1 && i.DaysOverdue <= 30 ? i.Outstanding : 0m),
                g.Sum(i => i.DaysOverdue >= 31 && i.DaysOverdue <= 60 ? i.Outstanding : 0m),
                g.Sum(i => i.DaysOverdue >= 61 && i.DaysOverdue <= 90 ? i.Outstanding : 0m),
                g.Sum(i => i.DaysOverdue > 90 ? i.Outstanding : 0m)))
            .SingleOrDefaultAsync(cancellationToken) ?? new ReceivablesAgeingDto(0, 0, 0, 0, 0);

        decimal ShareOf(decimal value) => sales == 0 ? 0m : Math.Round(value / sales, 4);

        // Top six states, then everything else together.
        var states = byState.Take(6).Select(s => new RankedItemDto(s.State, s.Value, ShareOf(s.Value), null)).ToList();
        if (byState.Count > 6)
        {
            var rest = byState.Skip(6).Sum(s => s.Value);
            states.Add(new RankedItemDto($"Other ({byState.Count - 6} states)", rest, ShareOf(rest), null));
        }

        return new DashboardDto(
            FinancialYear.Label(window.From),
            new DashboardPeriodDto(period, window.From, window.To, window.ComparisonFrom, window.ComparisonTo),
            new DashboardKpisDto(sales, comparison, invoiceCount, collected, paymentCount,
                outstanding, overdue, overdueCount, openOrdersValue, openOrderCount, lowStock),
            trend,
            [.. topCustomers.Select(c => new RankedItemDto(c.CustomerName, c.Value, ShareOf(c.Value), c.CustomerCity))],
            [.. topProducts.Select(p => new RankedItemDto(p.ProductName, p.Value, ShareOf(p.Value), $"{p.Quantity.ToString("#,##0.###", India)} {p.Unit}"))],
            states,
            ageing);
    }
}
