using SalesApp.AI.AskData;

namespace SalesApp.Tests.Ai;

public sealed class ColumnLabelTests
{
    [Theory]
    [InlineData("SalesValue", "Sales value")]
    [InlineData("Invoices", "Invoices")]
    [InlineData("Customer", "Customer")]
    [InlineData("FinancialYearStart", "Financial year start")]
    [InlineData("GSTAmount", "GST amount")]
    [InlineData("Sku", "SKU")]
    [InlineData("ProductSku", "Product SKU")]
    [InlineData("CustomerGstin", "Customer GSTIN")]
    [InlineData("total_outstanding", "Total outstanding")]
    [InlineData("Sales2025", "Sales 2025")]
    [InlineData("FY2025Sales", "FY 2025 sales")]
    [InlineData("Sales value", "Sales value")]
    [InlineData("cnt", "Cnt")]
    public void Turns_aliases_into_readable_headers(string alias, string expected) =>
        Assert.Equal(expected, ColumnLabel.From(alias));
}
