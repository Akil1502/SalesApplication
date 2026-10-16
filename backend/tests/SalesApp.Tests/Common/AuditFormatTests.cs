using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;

namespace SalesApp.Tests.Common;

public sealed class AuditFormatTests
{
    [Theory]
    [InlineData("CreditLimit", null, "Credit limit")]
    [InlineData("PaymentTermsDays", null, "Payment terms (days)")]
    [InlineData("AssignedRepId", null, "Sales rep")]
    [InlineData("CancelledAtUtc", null, "Cancelled at")]
    [InlineData("City", "BillingAddress", "Billing address: city")]
    [InlineData("Pincode", "ShippingAddress", "Shipping address: PIN code")]
    [InlineData("Gstin", null, "GSTIN")]
    public void Fields_are_named_as_people_read_them(string property, string? owner, string expected) =>
        Assert.Equal(expected, AuditFormat.Label(property, owner));

    [Fact]
    public void Values_are_written_as_people_read_them()
    {
        Assert.Equal("₹7,50,000", AuditFormat.Value(750000m, "CreditLimit"));
        Assert.Equal("₹1,234.5", AuditFormat.Value(1234.50m, "UnitPrice"));
        Assert.Equal("1,250.125", AuditFormat.Value(1250.125m, "Quantity"));
        Assert.Equal("18%", AuditFormat.Value(18m, "GstRate"));
        Assert.Equal("2.5%", AuditFormat.Value(2.50m, "DiscountPercent"));
        Assert.Equal("16 Oct 2026", AuditFormat.Value(new DateOnly(2026, 10, 16), "OrderDate"));
        Assert.Equal("16 Oct 2026 11:42", AuditFormat.Value(new DateTime(2026, 10, 16, 6, 12, 0, DateTimeKind.Utc), "CancelledAtUtc"));
        Assert.Equal("Partially paid", AuditFormat.Value(InvoiceStatus.PartiallyPaid, "Status"));
        Assert.Equal("Yes", AuditFormat.Value(true, "IsActive"));
        Assert.Null(AuditFormat.Value("  ", "Notes"));
        Assert.Null(AuditFormat.Value(null, "Notes"));
    }

    [Fact]
    public void Permissions_are_named_as_the_permission_list_shows_them()
    {
        Assert.Equal("Invoices: add & change", AuditFormat.Permission(Permissions.InvoicesEdit));
        Assert.Equal("Invoices: view", AuditFormat.Permission(Permissions.InvoicesView));
        Assert.Equal("AI usage", AuditFormat.Permission(Permissions.AiUsageView));
        Assert.Equal("See cost prices and margins", AuditFormat.Permission(Permissions.CostPricesView));
    }
}
