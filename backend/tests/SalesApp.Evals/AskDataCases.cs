using System.Globalization;
using SalesApp.Core.Common;

namespace SalesApp.Evals;

/// <summary>
/// A question and the reference query that answers it. <see cref="Required"/> lists the gold columns the answer
/// must reproduce (each inner array: any one of those columns); null means every column. Tokens in the SQL
/// ({FY}, {LastFY}, {FYStart}, {LastFYStart}, {Today}, {MonthStart}, {LastMonthStart}, {Months12Start}) are filled
/// in from the date of the run, the same "today" the question is asked on.
/// </summary>
public sealed record AskDataCase(string Id, string Question, string GoldSql, int[][]? Required = null)
{
    public string Sql(DateOnly today)
    {
        var fy = FinancialYear.StartYear(today);
        var month = new DateOnly(today.Year, today.Month, 1);
        string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return GoldSql
            .Replace("{FY}", fy.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{LastFY}", (fy - 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{FYStart}", Date(FinancialYear.Start(today)), StringComparison.Ordinal)
            .Replace("{LastFYStart}", Date(FinancialYear.Start(today).AddYears(-1)), StringComparison.Ordinal)
            .Replace("{Today}", Date(today), StringComparison.Ordinal)
            .Replace("{MonthStart}", Date(month), StringComparison.Ordinal)
            .Replace("{LastMonthStart}", Date(month.AddMonths(-1)), StringComparison.Ordinal)
            .Replace("{Months12Start}", Date(month.AddMonths(-12)), StringComparison.Ordinal);
    }
}

public static class AskDataCases
{
    private static readonly int[][] FirstOrSecondAndThird = [[0, 1], [2]];
    private static readonly int[][] SecondOnly = [[1]];

    public static readonly IReadOnlyList<AskDataCase> All =
    [
        new("sales-last-fy", "What were our total sales, excluding GST, in the last financial year?",
            "SELECT SUM(TaxableValue) FROM rpt.SalesLines WHERE FinancialYearStart = {LastFY}"),
        new("top5-customers-last-fy", "Who were our top 5 customers by sales in the last financial year, and how much did each buy?",
            "SELECT TOP 5 MAX(CustomerName), SUM(TaxableValue) AS Sales FROM rpt.SalesLines WHERE FinancialYearStart = {LastFY} GROUP BY CustomerId ORDER BY Sales DESC"),
        new("invoices-this-fy", "How many invoices have we raised so far this financial year?",
            "SELECT COUNT(*) FROM rpt.Invoices WHERE FinancialYearStart = {FY}"),
        new("receivables", "What is the total amount customers owe us right now?",
            "SELECT SUM(Outstanding) FROM rpt.Invoices WHERE Status IN ('Unpaid', 'PartiallyPaid')"),
        new("overdue-total", "How much money is overdue right now, meaning unpaid after the due date?",
            "SELECT SUM(Outstanding) FROM rpt.Invoices WHERE DaysOverdue > 0"),
        new("top-debtor", "Which customer owes us the most money right now, and how much?",
            "SELECT TOP 1 MAX(CustomerName), SUM(Outstanding) AS Owed FROM rpt.Invoices WHERE Outstanding > 0 GROUP BY CustomerId ORDER BY Owed DESC"),
        new("monthly-12", "Show our sales for each of the last 12 complete months.",
            "SELECT MonthStart, SUM(TaxableValue) FROM rpt.SalesLines WHERE MonthStart >= '{Months12Start}' AND MonthStart < '{MonthStart}' GROUP BY MonthStart",
            SecondOnly),
        new("state-sales-last-fy", "Break down last financial year's sales by state (place of supply).",
            "SELECT StateName, SUM(TaxableValue) FROM rpt.SalesLines WHERE FinancialYearStart = {LastFY} GROUP BY StateName"),
        new("out-of-stock", "Which products are out of stock right now?",
            "SELECT Sku, Name FROM rpt.Products WHERE StockStatus = 'OutOfStock' AND IsActive = 1",
            [[0, 1]]),
        new("active-customers", "How many active customers do we have?",
            "SELECT COUNT(*) FROM rpt.Customers WHERE IsActive = 1"),
        new("top5-products-fy", "What are our top 5 products by sales value this financial year?",
            "SELECT TOP 5 MAX(Sku), MAX(ProductName), SUM(TaxableValue) AS Sales FROM rpt.SalesLines WHERE FinancialYearStart = {FY} GROUP BY ProductId ORDER BY Sales DESC",
            FirstOrSecondAndThird),
        new("category-last-fy", "Show last financial year's sales by product category.",
            "SELECT CategoryName, SUM(TaxableValue) FROM rpt.SalesLines WHERE FinancialYearStart = {LastFY} GROUP BY CategoryName"),
        new("payments-last-month", "How much did we receive in customer payments last month?",
            "SELECT SUM(Amount) FROM rpt.Payments WHERE MonthStart = '{LastMonthStart}'"),
        new("payments-by-mode", "Break down the payments we've received this financial year by payment mode.",
            "SELECT Mode, SUM(Amount) FROM rpt.Payments WHERE PaymentDate >= '{FYStart}' GROUP BY Mode"),
        new("lapsed-customers", "Which customers bought from us last financial year but haven't bought anything this financial year?",
            """
            SELECT c.Name FROM rpt.Customers c
            WHERE EXISTS (SELECT 1 FROM rpt.Invoices i WHERE i.CustomerId = c.CustomerId AND i.FinancialYearStart = {LastFY})
              AND NOT EXISTS (SELECT 1 FROM rpt.Invoices i WHERE i.CustomerId = c.CustomerId AND i.FinancialYearStart = {FY})
            """),
        new("average-invoice", "What is our average invoice value, excluding GST, this financial year?",
            "SELECT AVG(TaxableValue) FROM rpt.Invoices WHERE FinancialYearStart = {FY}"),
        new("rep-sales", "How much has each sales rep sold this financial year?",
            "SELECT SalesRepName, SUM(TaxableValue) FROM rpt.SalesLines WHERE FinancialYearStart = {FY} GROUP BY SalesRepName",
            SecondOnly),
        new("best-month-last-fy", "Which month had the highest sales in the last financial year, and how much was it?",
            "SELECT TOP 1 MonthStart, SUM(TaxableValue) AS Sales FROM rpt.SalesLines WHERE FinancialYearStart = {LastFY} GROUP BY MonthStart ORDER BY Sales DESC",
            SecondOnly),
        new("gst-last-fy", "How much GST did we charge in total in the last financial year?",
            "SELECT SUM(TaxAmount) FROM rpt.SalesLines WHERE FinancialYearStart = {LastFY}"),
        new("overdue-60", "How many invoices are more than 60 days overdue?",
            "SELECT COUNT(*) FROM rpt.Invoices WHERE DaysOverdue > 60"),
        new("tiruppur-customers", "Which customers in Tiruppur have bought from us this financial year, and how much?",
            "SELECT MAX(CustomerName), SUM(TaxableValue) FROM rpt.SalesLines WHERE CustomerCity = 'Tiruppur' AND FinancialYearStart = {FY} GROUP BY CustomerId"),
        new("margin-by-category", "What was our gross margin by product category in the last financial year?",
            "SELECT CategoryName, SUM(GrossMargin) FROM rpt_mgr.Margins WHERE InvoiceDate >= '{LastFYStart}' AND InvoiceDate < '{FYStart}' GROUP BY CategoryName"),
        new("open-quotations", "How many quotations are still open, sent to the customer but not yet confirmed or cancelled?",
            "SELECT COUNT(*) FROM rpt.Orders WHERE Status = 'Quoted'"),
        new("polo-units-last-fy", "How many polo T-shirts (GT-TSP) did we sell in the last financial year?",
            "SELECT SUM(Quantity) FROM rpt.SalesLines WHERE Sku = 'GT-TSP' AND FinancialYearStart = {LastFY}"),
        new("low-stock", "Which products are running low: below their reorder level but not out of stock?",
            "SELECT Sku, Name FROM rpt.Products WHERE StockStatus = 'Low' AND IsActive = 1",
            [[0, 1]]),
    ];
}
