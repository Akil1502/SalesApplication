using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Reporting;

public interface IReportService
{
    IReadOnlyList<ReportDefinitionDto> List();
    Task<ReportResult> RunAsync(string key, ReportRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The standard reports, all computed from the rpt.* views so they agree with the dashboard and with each
/// other. Margin and stock-value columns appear for managers and admins only; the purchase reports need the right to
/// see supplier bills as well.
/// </summary>
public sealed class ReportService(AppDbContext db, IBusinessClock clock, ICurrentUser currentUser) : IReportService
{
    private static readonly ReportDefinitionDto[] Definitions =
    [
        new("sales-by-period", "Sales by period", "Monthly (or daily) invoiced sales, tax and invoice count.", true),
        new("sales-by-customer", "Sales by customer", "Who bought how much, ranked by sales value.", true),
        new("sales-by-product", "Sales by product", "Quantities and value per product; margins for managers.", true),
        new("gst-summary", "GST summary", "Taxable value and tax by B2B/B2C, intra/inter-state and rate, for GST returns.", true),
        new("hsn-summary", "HSN summary", "Quantity, taxable value and tax per HSN code (GSTR-1 table 12).", true),
        new("receivables-ageing", "Receivables ageing", "What each customer owes today, by how overdue it is.", false),
        new("stock", "Stock position", "Stock on hand and status per product; value at cost for managers.", false),
        new("purchase-register", "Purchase register", "Supplier bills with their taxable value and input GST, for GST returns.", true),
        new("payables-ageing", "Payables ageing", "What we owe each supplier today, by how overdue it is.", false),
        new("gst-net", "GST payable", "Output GST on sales less input credit on supplier bills, by month (before set-off rules).", true),
    ];

    /// <summary>Reports that need more than the reports right.</summary>
    private static readonly Dictionary<string, string> Needs = new()
    {
        ["purchase-register"] = Permissions.SupplierBillsView,
        ["payables-ageing"] = Permissions.SupplierBillsView,
        ["gst-net"] = Permissions.SupplierBillsView,
    };

    private static readonly SupplierBillStatus[] OpenBills = [SupplierBillStatus.Unpaid, SupplierBillStatus.PartiallyPaid];

    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public IReadOnlyList<ReportDefinitionDto> List() => [.. Definitions.Where(d => Allowed(d.Key))];

    public Task<ReportResult> RunAsync(string key, ReportRequest request, CancellationToken cancellationToken)
    {
        var definition = Definitions.FirstOrDefault(d => d.Key == key) ?? throw new NotFoundException($"There's no report called '{key}'.");
        if (!Allowed(key))
        {
            throw new ForbiddenException($"The {definition.Title} report needs the right to see supplier bills.");
        }

        var (from, to) = Range(request);
        return key switch
        {
            "sales-by-period" => SalesByPeriodAsync(definition, from, to, request.Period, cancellationToken),
            "sales-by-customer" => SalesByCustomerAsync(definition, from, to, cancellationToken),
            "sales-by-product" => SalesByProductAsync(definition, from, to, cancellationToken),
            "gst-summary" => GstSummaryAsync(definition, from, to, cancellationToken),
            "hsn-summary" => HsnSummaryAsync(definition, from, to, cancellationToken),
            "receivables-ageing" => AgeingAsync(definition, cancellationToken),
            "purchase-register" => PurchaseRegisterAsync(definition, from, to, cancellationToken),
            "payables-ageing" => PayablesAgeingAsync(definition, cancellationToken),
            "gst-net" => GstNetAsync(definition, from, to, cancellationToken),
            _ => StockAsync(definition, cancellationToken),
        };
    }

    private bool Allowed(string key) => !Needs.TryGetValue(key, out var permission) || currentUser.Has(permission);

    private (DateOnly From, DateOnly To) Range(ReportRequest request)
    {
        var today = clock.Today;
        var from = request.From ?? FinancialYear.Start(today);
        var to = request.To ?? today;
        if (from > to)
        {
            throw new ValidationException("from", "'From' must be on or before 'To'.");
        }

        if (to.DayNumber - from.DayNumber > 3660)
        {
            throw new ValidationException("from", "Reports can cover at most 10 years.");
        }

        return (from, to);
    }

    private IQueryable<RptSalesLine> Lines(DateOnly from, DateOnly to) =>
        db.RptSalesLines.Where(l => l.InvoiceDate >= from && l.InvoiceDate <= to);

    private async Task<ReportResult> SalesByPeriodAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, string? period, CancellationToken ct)
    {
        var daily = string.Equals(period, "day", StringComparison.OrdinalIgnoreCase);
        var rows = daily
            ? await Lines(from, to).GroupBy(l => l.InvoiceDate)
                .Select(g => new { Period = g.Key, Invoices = g.Select(l => l.InvoiceId).Distinct().Count(), Taxable = g.Sum(l => l.TaxableValue), Tax = g.Sum(l => l.TaxAmount) })
                .OrderBy(r => r.Period).ToListAsync(ct)
            : await Lines(from, to).GroupBy(l => l.MonthStart)
                .Select(g => new { Period = g.Key, Invoices = g.Select(l => l.InvoiceId).Distinct().Count(), Taxable = g.Sum(l => l.TaxableValue), Tax = g.Sum(l => l.TaxAmount) })
                .OrderBy(r => r.Period).ToListAsync(ct);

        ReportColumn[] columns =
        [
            new("period", daily ? "Date" : "Month", daily ? ReportColumnType.Date : ReportColumnType.Text),
            new("invoices", "Invoices", ReportColumnType.Count),
            new("taxable", "Taxable value", ReportColumnType.Money),
            new("tax", "GST", ReportColumnType.Money),
            new("total", "Invoice value", ReportColumnType.Money),
        ];

        return Result(d, from, to, columns, rows.Select(r => Row(
            ("period", daily ? r.Period : r.Period.ToString("MMM yyyy", India)),
            ("invoices", r.Invoices), ("taxable", r.Taxable), ("tax", r.Tax), ("total", r.Taxable + r.Tax))).ToList());
    }

    private async Task<ReportResult> SalesByCustomerAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await Lines(from, to)
            .GroupBy(l => new { l.CustomerId, l.CustomerCode, l.CustomerName, l.StateName })
            .Select(g => new
            {
                g.Key.CustomerCode, g.Key.CustomerName, g.Key.StateName,
                Invoices = g.Select(l => l.InvoiceId).Distinct().Count(),
                Taxable = g.Sum(l => l.TaxableValue), Tax = g.Sum(l => l.TaxAmount),
            })
            .OrderByDescending(r => r.Taxable)
            .ToListAsync(ct);
        var total = rows.Sum(r => r.Taxable);

        ReportColumn[] columns =
        [
            new("code", "Code", ReportColumnType.Text),
            new("customer", "Customer", ReportColumnType.Text),
            new("state", "State", ReportColumnType.Text),
            new("invoices", "Invoices", ReportColumnType.Count),
            new("taxable", "Taxable value", ReportColumnType.Money),
            new("tax", "GST", ReportColumnType.Money),
            new("total", "Invoice value", ReportColumnType.Money),
            new("share", "Share", ReportColumnType.Percent),
        ];

        return Result(d, from, to, columns, rows.Select(r => Row(
            ("code", r.CustomerCode), ("customer", r.CustomerName), ("state", r.StateName), ("invoices", r.Invoices),
            ("taxable", r.Taxable), ("tax", r.Tax), ("total", r.Taxable + r.Tax), ("share", Share(r.Taxable, total)))).ToList(),
            totals => totals["share"] = rows.Count > 0 ? 1m : 0m);
    }

    private async Task<ReportResult> SalesByProductAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await Lines(from, to)
            .GroupBy(l => new { l.ProductId, l.Sku, l.ProductName, l.CategoryName, l.Unit })
            .Select(g => new { g.Key.ProductId, g.Key.Sku, g.Key.ProductName, g.Key.CategoryName, g.Key.Unit, Quantity = g.Sum(l => l.Quantity), Taxable = g.Sum(l => l.TaxableValue) })
            .OrderByDescending(r => r.Taxable)
            .ToListAsync(ct);
        var total = rows.Sum(r => r.Taxable);

        var showMargin = currentUser.Has(Permissions.CostPricesView);
        var margins = showMargin
            ? await db.RptMargins.Where(m => m.InvoiceDate >= from && m.InvoiceDate <= to)
                .GroupBy(m => m.ProductId).Select(g => new MarginTotal(g.Key, g.Sum(m => m.CostValue), g.Sum(m => m.GrossMargin)))
                .ToDictionaryAsync(m => m.ProductId, ct)
            : new Dictionary<int, MarginTotal>();

        var columns = new List<ReportColumn>
        {
            new("sku", "SKU", ReportColumnType.Text),
            new("product", "Product", ReportColumnType.Text),
            new("category", "Category", ReportColumnType.Text),
            new("unit", "Unit", ReportColumnType.Text),
            new("quantity", "Quantity", ReportColumnType.Quantity),
            new("taxable", "Taxable value", ReportColumnType.Money),
            new("averageRate", "Average rate", ReportColumnType.Money),
            new("share", "Share", ReportColumnType.Percent),
        };
        if (showMargin)
        {
            columns.Add(new("cost", "Cost (current)", ReportColumnType.Money));
            columns.Add(new("margin", "Gross margin", ReportColumnType.Money));
            columns.Add(new("marginPercent", "Margin %", ReportColumnType.Percent));
        }

        var result = rows.Select(r =>
        {
            var row = Row(("sku", r.Sku), ("product", r.ProductName), ("category", r.CategoryName), ("unit", r.Unit), ("quantity", r.Quantity),
                ("taxable", r.Taxable), ("averageRate", r.Quantity == 0 ? 0m : Math.Round(r.Taxable / r.Quantity, 2)), ("share", Share(r.Taxable, total)));
            if (showMargin)
            {
                var m = margins.GetValueOrDefault(r.ProductId);
                row["cost"] = m?.Cost;
                row["margin"] = m?.Margin;
                row["marginPercent"] = m is null ? null : Share(m.Margin, r.Taxable);
            }

            return row;
        }).ToList();

        return Result(d, from, to, columns, result, totals =>
        {
            totals["averageRate"] = null; // averages of different units don't add up
            totals["quantity"] = null;
            totals["share"] = rows.Count > 0 ? 1m : 0m;
            if (showMargin && totals.GetValueOrDefault("margin") is decimal margin)
            {
                totals["marginPercent"] = Share(margin, total);
            }
        });
    }

    private async Task<ReportResult> GstSummaryAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await Lines(from, to)
            .GroupBy(l => new { l.SupplyType, l.IsInterState, l.GstRate })
            .Select(g => new { g.Key.SupplyType, g.Key.IsInterState, g.Key.GstRate, Taxable = g.Sum(l => l.TaxableValue), Cgst = g.Sum(l => l.CgstAmount), Sgst = g.Sum(l => l.SgstAmount), Igst = g.Sum(l => l.IgstAmount) })
            .OrderBy(r => r.SupplyType).ThenBy(r => r.IsInterState).ThenBy(r => r.GstRate)
            .ToListAsync(ct);

        ReportColumn[] columns =
        [
            new("supplyType", "Supply", ReportColumnType.Text),
            new("nature", "Nature", ReportColumnType.Text),
            new("rate", "GST rate", ReportColumnType.Percent),
            new("taxable", "Taxable value", ReportColumnType.Money),
            new("cgst", "CGST", ReportColumnType.Money),
            new("sgst", "SGST/UTGST", ReportColumnType.Money),
            new("igst", "IGST", ReportColumnType.Money),
            new("tax", "Total GST", ReportColumnType.Money),
        ];

        return Result(d, from, to, columns, rows.Select(r => Row(
            ("supplyType", r.SupplyType), ("nature", r.IsInterState ? "Inter-state" : "Intra-state"), ("rate", r.GstRate / 100m),
            ("taxable", r.Taxable), ("cgst", r.Cgst), ("sgst", r.Sgst), ("igst", r.Igst), ("tax", r.Cgst + r.Sgst + r.Igst))).ToList(),
            totals => totals["rate"] = null);
    }

    private async Task<ReportResult> HsnSummaryAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await Lines(from, to)
            .GroupBy(l => new { l.HsnCode, l.Unit, l.GstRate })
            .Select(g => new { g.Key.HsnCode, g.Key.Unit, g.Key.GstRate, Quantity = g.Sum(l => l.Quantity), Taxable = g.Sum(l => l.TaxableValue), Cgst = g.Sum(l => l.CgstAmount), Sgst = g.Sum(l => l.SgstAmount), Igst = g.Sum(l => l.IgstAmount) })
            .OrderBy(r => r.HsnCode).ThenBy(r => r.Unit)
            .ToListAsync(ct);

        ReportColumn[] columns =
        [
            new("hsn", "HSN", ReportColumnType.Text),
            new("unit", "Unit", ReportColumnType.Text),
            new("rate", "GST rate", ReportColumnType.Percent),
            new("quantity", "Quantity", ReportColumnType.Quantity),
            new("taxable", "Taxable value", ReportColumnType.Money),
            new("cgst", "CGST", ReportColumnType.Money),
            new("sgst", "SGST/UTGST", ReportColumnType.Money),
            new("igst", "IGST", ReportColumnType.Money),
            new("tax", "Total GST", ReportColumnType.Money),
        ];

        return Result(d, from, to, columns, rows.Select(r => Row(
            ("hsn", r.HsnCode), ("unit", r.Unit), ("rate", r.GstRate / 100m), ("quantity", r.Quantity), ("taxable", r.Taxable),
            ("cgst", r.Cgst), ("sgst", r.Sgst), ("igst", r.Igst), ("tax", r.Cgst + r.Sgst + r.Igst))).ToList(),
            totals =>
            {
                totals["rate"] = null;
                totals["quantity"] = null;
            });
    }

    private async Task<ReportResult> AgeingAsync(ReportDefinitionDto d, CancellationToken ct)
    {
        var rows = await db.RptInvoices
            .Where(i => i.Outstanding > 0)
            .GroupBy(i => new { i.CustomerId, i.CustomerName })
            .Select(g => new
            {
                g.Key.CustomerName,
                Invoices = g.Count(),
                NotDue = g.Sum(i => i.DaysOverdue == 0 ? i.Outstanding : 0m),
                D30 = g.Sum(i => i.DaysOverdue >= 1 && i.DaysOverdue <= 30 ? i.Outstanding : 0m),
                D60 = g.Sum(i => i.DaysOverdue >= 31 && i.DaysOverdue <= 60 ? i.Outstanding : 0m),
                D90 = g.Sum(i => i.DaysOverdue >= 61 && i.DaysOverdue <= 90 ? i.Outstanding : 0m),
                Over90 = g.Sum(i => i.DaysOverdue > 90 ? i.Outstanding : 0m),
                Total = g.Sum(i => i.Outstanding),
                MaxDays = g.Max(i => i.DaysOverdue),
            })
            .OrderByDescending(r => r.Total)
            .ToListAsync(ct);

        ReportColumn[] columns =
        [
            new("customer", "Customer", ReportColumnType.Text),
            new("invoices", "Open invoices", ReportColumnType.Count),
            new("notDue", "Not yet due", ReportColumnType.Money),
            new("days1To30", "1–30 days", ReportColumnType.Money),
            new("days31To60", "31–60 days", ReportColumnType.Money),
            new("days61To90", "61–90 days", ReportColumnType.Money),
            new("over90", "Over 90 days", ReportColumnType.Money),
            new("total", "Total due", ReportColumnType.Money),
            new("oldest", "Most overdue (days)", ReportColumnType.Count),
        ];

        var today = clock.Today;
        return Result(d with { Description = $"Outstanding as of {today.ToString("dd MMM yyyy", India)}." }, null, null, columns, rows.Select(r => Row(
            ("customer", r.CustomerName), ("invoices", r.Invoices), ("notDue", r.NotDue), ("days1To30", r.D30), ("days31To60", r.D60),
            ("days61To90", r.D90), ("over90", r.Over90), ("total", r.Total), ("oldest", r.MaxDays))).ToList(),
            totals => totals["oldest"] = rows.Count == 0 ? null : rows.Max(r => r.MaxDays));
    }

    private async Task<ReportResult> StockAsync(ReportDefinitionDto d, CancellationToken ct)
    {
        var showValue = currentUser.Has(Permissions.CostPricesView);
        var rows = await db.RptProducts
            .Where(p => p.IsActive)
            .OrderBy(p => p.CategoryName).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Sku, p.Name, p.CategoryName, p.Unit, p.QuantityOnHand, p.ReorderLevel, p.StockStatus,
                Cost = db.Products.Where(x => x.Id == p.ProductId).Select(x => x.CostPrice).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var columns = new List<ReportColumn>
        {
            new("sku", "SKU", ReportColumnType.Text),
            new("product", "Product", ReportColumnType.Text),
            new("category", "Category", ReportColumnType.Text),
            new("unit", "Unit", ReportColumnType.Text),
            new("onHand", "On hand", ReportColumnType.Quantity),
            new("reorderLevel", "Reorder level", ReportColumnType.Quantity),
            new("status", "Status", ReportColumnType.Text),
        };
        if (showValue)
        {
            columns.Add(new("cost", "Cost per unit", ReportColumnType.Money));
            columns.Add(new("value", "Value at cost", ReportColumnType.Money));
        }

        var result = rows.Select(r =>
        {
            var row = Row(("sku", r.Sku), ("product", r.Name), ("category", r.CategoryName), ("unit", r.Unit), ("onHand", r.QuantityOnHand),
                ("reorderLevel", r.ReorderLevel), ("status", r.StockStatus switch { "OutOfStock" => "Out of stock", "Low" => "Low", _ => "In stock" }));
            if (showValue)
            {
                row["cost"] = r.Cost;
                row["value"] = r.Cost is { } cost ? Math.Round(Math.Max(r.QuantityOnHand, 0) * cost, 2) : null;
            }

            return row;
        }).ToList();

        return Result(d with { Description = $"As of {clock.Today.ToString("dd MMM yyyy", India)}." }, null, null, columns, result, totals =>
        {
            totals["onHand"] = null;
            totals["reorderLevel"] = null;
            totals["cost"] = null;
        });
    }

    private async Task<ReportResult> PurchaseRegisterAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.SupplierBills.AsNoTracking()
            .Where(b => b.Status != SupplierBillStatus.Cancelled && b.BillDate >= from && b.BillDate <= to)
            .OrderBy(b => b.BillDate).ThenBy(b => b.Id)
            .Select(b => new
            {
                b.BillDate, b.Number, b.SupplierInvoiceNumber, b.SupplierName, b.SupplierGstin, Order = b.PurchaseOrder!.Number,
                Taxable = b.TaxableValue + b.ChargesAmount, b.CgstAmount, b.SgstAmount, b.IgstAmount, b.GrandTotal,
            })
            .ToListAsync(ct);

        ReportColumn[] columns =
        [
            new("date", "Bill date", ReportColumnType.Date),
            new("number", "Our ref", ReportColumnType.Text),
            new("invoice", "Supplier's invoice", ReportColumnType.Text),
            new("supplier", "Supplier", ReportColumnType.Text),
            new("gstin", "GSTIN", ReportColumnType.Text),
            new("order", "Purchase order", ReportColumnType.Text),
            new("taxable", "Taxable value", ReportColumnType.Money),
            new("cgst", "CGST", ReportColumnType.Money),
            new("sgst", "SGST/UTGST", ReportColumnType.Money),
            new("igst", "IGST", ReportColumnType.Money),
            new("tax", "Input GST", ReportColumnType.Money),
            new("total", "Bill total", ReportColumnType.Money),
        ];

        return Result(d, from, to, columns, rows.Select(r => Row(
            ("date", r.BillDate), ("number", r.Number), ("invoice", r.SupplierInvoiceNumber), ("supplier", r.SupplierName), ("gstin", r.SupplierGstin),
            ("order", r.Order), ("taxable", r.Taxable), ("cgst", r.CgstAmount), ("sgst", r.SgstAmount), ("igst", r.IgstAmount),
            ("tax", r.CgstAmount + r.SgstAmount + r.IgstAmount), ("total", r.GrandTotal))).ToList());
    }

    private async Task<ReportResult> PayablesAgeingAsync(ReportDefinitionDto d, CancellationToken ct)
    {
        var today = clock.Today;
        var bills = await db.SupplierBills.AsNoTracking()
            .Where(b => OpenBills.Contains(b.Status))
            .Select(b => new { b.SupplierId, b.SupplierName, b.DueDate, Outstanding = b.GrandTotal - b.AmountPaid })
            .ToListAsync(ct);

        var rows = bills
            .Select(b => (b.SupplierId, b.SupplierName, b.Outstanding, Days: b.DueDate < today ? today.DayNumber - b.DueDate.DayNumber : 0))
            .GroupBy(b => (b.SupplierId, b.SupplierName))
            .Select(g => new
            {
                Supplier = g.Key.SupplierName,
                Bills = g.Count(),
                NotDue = g.Where(b => b.Days == 0).Sum(b => b.Outstanding),
                D30 = g.Where(b => b.Days is >= 1 and <= 30).Sum(b => b.Outstanding),
                D60 = g.Where(b => b.Days is >= 31 and <= 60).Sum(b => b.Outstanding),
                D90 = g.Where(b => b.Days is >= 61 and <= 90).Sum(b => b.Outstanding),
                Over90 = g.Where(b => b.Days > 90).Sum(b => b.Outstanding),
                Total = g.Sum(b => b.Outstanding),
                MaxDays = g.Max(b => b.Days),
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        ReportColumn[] columns =
        [
            new("supplier", "Supplier", ReportColumnType.Text),
            new("bills", "Open bills", ReportColumnType.Count),
            new("notDue", "Not yet due", ReportColumnType.Money),
            new("days1To30", "1–30 days", ReportColumnType.Money),
            new("days31To60", "31–60 days", ReportColumnType.Money),
            new("days61To90", "61–90 days", ReportColumnType.Money),
            new("over90", "Over 90 days", ReportColumnType.Money),
            new("total", "Total owed", ReportColumnType.Money),
            new("oldest", "Most overdue (days)", ReportColumnType.Count),
        ];

        return Result(d with { Description = $"Owed as of {today.ToString("dd MMM yyyy", India)}." }, null, null, columns, rows.Select(r => Row(
            ("supplier", r.Supplier), ("bills", r.Bills), ("notDue", r.NotDue), ("days1To30", r.D30), ("days31To60", r.D60),
            ("days61To90", r.D90), ("over90", r.Over90), ("total", r.Total), ("oldest", r.MaxDays))).ToList(),
            totals => totals["oldest"] = rows.Count == 0 ? null : rows.Max(r => r.MaxDays));
    }

    private async Task<ReportResult> GstNetAsync(ReportDefinitionDto d, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var output = await Lines(from, to)
            .GroupBy(l => l.MonthStart)
            .Select(g => new { Month = g.Key, Cgst = g.Sum(l => l.CgstAmount), Sgst = g.Sum(l => l.SgstAmount), Igst = g.Sum(l => l.IgstAmount) })
            .ToListAsync(ct);
        var input = (await db.SupplierBills.AsNoTracking()
                .Where(b => b.Status != SupplierBillStatus.Cancelled && b.BillDate >= from && b.BillDate <= to)
                .Select(b => new { b.BillDate, b.CgstAmount, b.SgstAmount, b.IgstAmount })
                .ToListAsync(ct))
            .GroupBy(b => new DateOnly(b.BillDate.Year, b.BillDate.Month, 1))
            .ToDictionary(g => g.Key, g => (Cgst: g.Sum(b => b.CgstAmount), Sgst: g.Sum(b => b.SgstAmount), Igst: g.Sum(b => b.IgstAmount)));
        var sales = output.ToDictionary(o => o.Month, o => (o.Cgst, o.Sgst, o.Igst));

        ReportColumn[] columns =
        [
            new("month", "Month", ReportColumnType.Text),
            new("outputCgst", "Output CGST", ReportColumnType.Money),
            new("outputSgst", "Output SGST", ReportColumnType.Money),
            new("outputIgst", "Output IGST", ReportColumnType.Money),
            new("output", "Output GST", ReportColumnType.Money),
            new("inputCgst", "Input CGST", ReportColumnType.Money),
            new("inputSgst", "Input SGST", ReportColumnType.Money),
            new("inputIgst", "Input IGST", ReportColumnType.Money),
            new("input", "Input credit", ReportColumnType.Money),
            new("net", "Net payable", ReportColumnType.Money),
        ];

        var rows = sales.Keys.Union(input.Keys).Order().Select(month =>
        {
            var o = sales.GetValueOrDefault(month);
            var i = input.GetValueOrDefault(month);
            var outputTax = o.Cgst + o.Sgst + o.Igst;
            var inputTax = i.Cgst + i.Sgst + i.Igst;
            return Row(("month", month.ToString("MMM yyyy", India)),
                ("outputCgst", o.Cgst), ("outputSgst", o.Sgst), ("outputIgst", o.Igst), ("output", outputTax),
                ("inputCgst", i.Cgst), ("inputSgst", i.Sgst), ("inputIgst", i.Igst), ("input", inputTax),
                ("net", outputTax - inputTax));
        }).ToList();

        return Result(d, from, to, columns, rows);
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private sealed record MarginTotal(int ProductId, decimal Cost, decimal Margin);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) => cells.ToDictionary(c => c.Key, c => c.Value);

    private static decimal Share(decimal part, decimal whole) => whole == 0 ? 0m : Math.Round(part / whole, 4);

    /// <summary>Builds the result and a totals row that sums every numeric column; <paramref name="adjustTotals"/> fixes the rest.</summary>
    private static ReportResult Result(ReportDefinitionDto d, DateOnly? from, DateOnly? to, IReadOnlyList<ReportColumn> columns,
        List<Dictionary<string, object?>> rows, Action<Dictionary<string, object?>>? adjustTotals = null)
    {
        var totals = new Dictionary<string, object?> { [columns[0].Key] = "Total" };
        foreach (var column in columns.Skip(1))
        {
            totals[column.Key] = column.Type switch
            {
                ReportColumnType.Money or ReportColumnType.Quantity => rows.Sum(r => r.GetValueOrDefault(column.Key) as decimal? ?? 0m),
                ReportColumnType.Count => rows.Sum(r => r.GetValueOrDefault(column.Key) as int? ?? 0),
                _ => null,
            };
        }

        adjustTotals?.Invoke(totals);
        var subtitle = from is { } f && to is { } t
            ? $"{f.ToString("dd MMM yyyy", India)} to {t.ToString("dd MMM yyyy", India)}"
            : d.Description;
        return new ReportResult(d.Key, d.Title, subtitle, from, to, columns, rows, totals);
    }
}
