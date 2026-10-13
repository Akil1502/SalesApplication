using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SalesApp.Infrastructure.Reporting;

// Read models over the rpt.* views (Sql/001_reporting_views.sql). Reports and the dashboard read these,
// so every screen agrees with every other screen (and with the AI, which queries the same views).

public sealed class RptSalesLine
{
    public int InvoiceLineId { get; set; }
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly MonthStart { get; set; }
    public int FinancialYearStart { get; set; }
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerCity { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public string StateName { get; set; } = string.Empty;
    public string SupplyType { get; set; } = string.Empty;
    public bool IsInterState { get; set; }
    public string? SalesRepName { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class RptInvoice
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly MonthStart { get; set; }
    public int FinancialYearStart { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string StateName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TaxableValue { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Outstanding { get; set; }
    public int DaysOverdue { get; set; }
}

public sealed class RptProduct
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal ListPrice { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal QuantityOnHand { get; set; }
    public string StockStatus { get; set; } = string.Empty;
    public DateTime? LastMovementAtUtc { get; set; }
    public bool IsActive { get; set; }
}

public sealed class RptMargin
{
    public int InvoiceLineId { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal CostValue { get; set; }
    public decimal GrossMargin { get; set; }
}

public sealed class RptPayment
{
    public int PaymentId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string Mode { get; set; } = string.Empty;
}

internal static class ReportingViewMappings
{
    public static void Map(ModelBuilder builder)
    {
        View<RptSalesLine>(builder, "SalesLines");
        View<RptInvoice>(builder, "Invoices");
        View<RptProduct>(builder, "Products");
        View<RptPayment>(builder, "Payments");
        View<RptMargin>(builder, "Margins", "rpt_mgr");
    }

    private static EntityTypeBuilder<T> View<T>(ModelBuilder builder, string name, string schema = "rpt") where T : class =>
        builder.Entity<T>().HasNoKey().ToView(name, schema);
}
