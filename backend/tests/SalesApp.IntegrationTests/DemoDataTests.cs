using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.DemoData;

namespace SalesApp.IntegrationTests;

/// <summary>
/// Its own database with 14 months of generated history, untouched by other tests, so totals can be compared exactly.
/// </summary>
public sealed class DemoDataFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"SalesApp_Demo_{Guid.NewGuid():N}";

    public string ConnectionString => TestServer.ConnectionString(_databaseName);

    public DemoDataSummary Summary { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Ai:Insights:NightlyEnabled", "false");
        builder.UseSetting("Security:LoginAttemptsPerMinute", "100000"); // tests sign in hundreds of times a minute
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-0123456789");
        builder.UseSetting("Seed:AdminEmail", "admin@salesapp.test");
        builder.UseSetting("Seed:AdminPassword", "Integration-Test-Admin-1");
        builder.UseSetting("Seed:DemoUserPassword", "Integration-Test-Demo-1");
    }

    public async ValueTask InitializeAsync()
    {
        await DbInitializer.InitializeAsync(Services, TestContext.Current.CancellationToken);
        using var scope = Services.CreateScope();
        Summary = await scope.ServiceProvider.GetRequiredService<DemoDataGenerator>()
            .GenerateAsync(new DemoDataOptions(Months: 14, Seed: 42), TestContext.Current.CancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }
}

public sealed class DemoDataTests(DemoDataFactory factory) : IClassFixture<DemoDataFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public void Generation_is_fast_and_produces_a_realistic_volume()
    {
        Assert.True(factory.Summary.Elapsed < TimeSpan.FromMinutes(1), $"took {factory.Summary.Elapsed}");
        Assert.InRange(factory.Summary.Invoices, 1000, 3000);
        Assert.Equal(26, factory.Summary.Products);
        Assert.True(factory.Summary.Payments > factory.Summary.Invoices * 0.7);
    }

    [Fact]
    public async Task Generator_refuses_to_overwrite_existing_data_without_reset()
    {
        using var scope = factory.Services.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<DemoDataGenerator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(new DemoDataOptions(Months: 2), Ct));
    }

    [Fact]
    public async Task Demo_opens_with_products_to_reorder_and_recent_orders_in_the_pipeline()
    {
        var low = await WithDbAsync(db => db.StockLevels.Where(s => s.QuantityOnHand <= s.Product!.ReorderLevel).Select(s => s.QuantityOnHand).ToListAsync(Ct));
        Assert.True(low.Count >= 3, $"{low.Count} products at or below reorder level");
        Assert.Contains(0m, low);
        Assert.Contains(low, q => q > 0);

        var today = factory.Services.GetRequiredService<SalesApp.Core.Common.IBusinessClock>().Today;
        var open = await WithDbAsync(db => db.SalesOrders
            .Where(o => o.Status == SalesOrderStatus.Draft || o.Status == SalesOrderStatus.Quoted || o.Status == SalesOrderStatus.Confirmed)
            .Select(o => o.OrderDate).ToListAsync(Ct));
        Assert.NotEmpty(open);
        Assert.All(open, date => Assert.True(date >= today.AddDays(-10), $"order from {date} still open"));
    }

    [Fact]
    public async Task Stock_ledger_adds_up_and_never_goes_negative()
    {
        var mismatches = await WithDbAsync(db => db.StockLevels
            .Where(s => db.StockMovements.Where(m => m.ProductId == s.ProductId).Sum(m => m.Quantity) != s.QuantityOnHand)
            .CountAsync(Ct));
        var negative = await WithDbAsync(db => db.StockMovements.CountAsync(m => m.BalanceAfter < 0, Ct));

        Assert.Equal(0, mismatches);
        Assert.Equal(0, negative);
    }

    [Fact]
    public async Task Invoices_are_numbered_without_gaps_and_paid_amounts_match_payments()
    {
        var invoices = await WithDbAsync(db => db.Invoices.Select(i => new { i.FinancialYear, i.Number, i.AmountPaid, Paid = i.Payments.Sum(p => p.Amount), i.Status, i.GrandTotal }).ToListAsync(Ct));

        foreach (var year in invoices.GroupBy(i => i.FinancialYear))
        {
            var numbers = year.Select(i => int.Parse(i.Number[^5..], System.Globalization.CultureInfo.InvariantCulture)).Order();
            Assert.Equal(Enumerable.Range(1, year.Count()), numbers);
        }

        Assert.All(invoices, i => Assert.Equal(i.Paid, i.AmountPaid));
        Assert.All(invoices.Where(i => i.Status == InvoiceStatus.Paid), i => Assert.Equal(i.GrandTotal, i.AmountPaid));
    }

    [Fact]
    public async Task Festive_months_outsell_the_early_summer_lull()
    {
        var monthly = await WithDbAsync(db => db.RptSalesLines.GroupBy(l => l.MonthStart.Month).Select(g => new { Month = g.Key, Value = g.Sum(l => l.TaxableValue) }).ToListAsync(Ct));
        decimal Average(params int[] months) => monthly.Where(m => months.Contains(m.Month)).Average(m => m.Value);

        Assert.True(Average(10, 11) > Average(4, 5, 6) * 1.25m, "October-November should clearly beat April-June");
    }

    [Fact]
    public async Task Every_sales_report_agrees_with_the_dashboard_and_the_invoices()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var dashboard = await (await manager.GetAsync("/api/dashboard", Ct)).ReadAsync<DashboardDto>();

        // Reports default to the financial year to date, the same window as the dashboard's FY figure.
        var expected = dashboard.Kpis.SalesFinancialYearToDate;
        Assert.True(expected > 0);
        foreach (var report in new[] { "sales-by-period", "sales-by-customer", "sales-by-product", "gst-summary", "hsn-summary" })
        {
            var result = await (await manager.GetAsync($"/api/reports/{report}", Ct)).ReadAsync<ReportResult>();
            Assert.Equal(expected, ((System.Text.Json.JsonElement)result.Totals["taxable"]!).GetDecimal());
        }

        var period = await (await manager.GetAsync("/api/reports/sales-by-period", Ct)).ReadAsync<ReportResult>();
        var fromInvoices = await WithDbAsync(db => db.Invoices
            .Where(i => i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= period.From && i.InvoiceDate <= period.To)
            .SumAsync(i => i.TaxableValue, Ct));
        Assert.Equal(expected, fromInvoices);
    }

    [Fact]
    public async Task Ageing_report_matches_dashboard_receivables()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var dashboard = await (await manager.GetAsync("/api/dashboard", Ct)).ReadAsync<DashboardDto>();
        var ageing = await (await manager.GetAsync("/api/reports/receivables-ageing", Ct)).ReadAsync<ReportResult>();

        var total = ((System.Text.Json.JsonElement)ageing.Totals["total"]!).GetDecimal();
        Assert.Equal(dashboard.Kpis.Outstanding, total);
        var buckets = dashboard.Ageing;
        Assert.Equal(total, buckets.NotDue + buckets.Days1To30 + buckets.Days31To60 + buckets.Days61To90 + buckets.Over90);
        Assert.Equal(dashboard.Kpis.Overdue, total - buckets.NotDue);
    }

    [Fact]
    public async Task Sales_reps_dont_see_margins_or_stock_value()
    {
        var (rep, _) = await factory.CreateClientAsync(Roles.SalesRep);
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        var repProducts = await (await rep.GetAsync("/api/reports/sales-by-product", Ct)).ReadAsync<ReportResult>();
        var managerProducts = await (await manager.GetAsync("/api/reports/sales-by-product", Ct)).ReadAsync<ReportResult>();
        var repStock = await (await rep.GetAsync("/api/reports/stock", Ct)).ReadAsync<ReportResult>();

        Assert.DoesNotContain(repProducts.Columns, c => c.Key == "margin");
        Assert.Contains(managerProducts.Columns, c => c.Key == "margin");
        Assert.DoesNotContain(repStock.Columns, c => c.Key == "value");
    }

    [Fact]
    public async Task Reports_export_to_excel_with_the_same_totals()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var json = await (await manager.GetAsync("/api/reports/sales-by-customer", Ct)).ReadAsync<ReportResult>();

        var response = await manager.GetAsync("/api/reports/sales-by-customer/excel", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync(Ct));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Customer", sheet.Cell(4, 2).GetString());
        var totalsRow = 4 + json.Rows.Count + 1;
        Assert.Equal("Total", sheet.Cell(totalsRow, 1).GetString());
        Assert.Equal(((System.Text.Json.JsonElement)json.Totals["taxable"]!).GetDecimal(), (decimal)sheet.Cell(totalsRow, 5).GetDouble(), 2);
    }

    [Fact]
    public async Task Unknown_report_is_404_and_bad_ranges_are_rejected()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/reports/does-not-exist", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/reports/sales-by-period?from=2026-05-01&to=2026-04-01", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("SELECT COUNT(*) FROM rpt.SalesLines", true)]
    [InlineData("SELECT COUNT(*) FROM rpt.Customers", true)]
    [InlineData("SELECT TOP 1 Phone FROM dbo.Customers", false)]
    [InlineData("SELECT TOP 1 PasswordHash FROM dbo.AspNetUsers", false)]
    [InlineData("SELECT TOP 1 CostValue FROM rpt_mgr.Margins", false)]
    [InlineData("UPDATE rpt.Products SET ListPrice = 1", false)]
    [InlineData("DELETE FROM dbo.Payments", false)]
    public async Task Ai_reader_database_user_can_only_read_reporting_views(string sql, bool allowed)
    {
        // Phase P6 runs AI-written SQL as this user; the database itself must refuse anything outside rpt.*.
        await using var connection = new SqlConnection(factory.ConnectionString + ";Pooling=false");
        await connection.OpenAsync(Ct);
        await using (var impersonate = new SqlCommand("EXECUTE AS USER = 'ai_reader' WITH NO REVERT;", connection))
        {
            await impersonate.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new SqlCommand(sql, connection);
        if (allowed)
        {
            Assert.NotNull(await command.ExecuteScalarAsync(Ct));
        }
        else
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync(Ct));
            Assert.Equal(229, error.Number); // permission denied
        }
    }

    [Fact]
    public async Task Invoices_endpoint_works_on_generated_data()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var response = await manager.GetAsync("/api/invoices?overdueOnly=true&pageSize=5", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await (await manager.GetAsync("/api/invoices/summary", Ct)).Content.ReadFromJsonAsync<SalesApp.Core.Contracts.Sales.InvoiceSummaryDto>(TestClients.Json, Ct);
        Assert.True(summary!.OutstandingAmount > 0);
    }
}
