using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.Property(o => o.Number).HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.PlaceOfSupplyStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(o => o.CustomerReference).HasMaxLength(50);
        builder.Property(o => o.Notes).HasMaxLength(2000);
        builder.Property(o => o.QuotationNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => o.QuotationNumber).IsUnique().HasFilter("[QuotationNumber] IS NOT NULL");
        builder.HasIndex(o => new { o.CustomerId, o.Status });
        builder.HasIndex(o => o.OrderDate);

        builder.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.CreatedBy).WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<State>().WithMany().HasForeignKey(o => o.PlaceOfSupplyStateCode).OnDelete(DeleteBehavior.Restrict);
        // The order points at its current invoice; the invoice points back at its order (separate relationship).
        builder.HasOne<Invoice>().WithMany().HasForeignKey(o => o.InvoiceId).OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        builder.Property(l => l.GstRate).HasPrecision(5, 2);
        builder.HasIndex(l => new { l.SalesOrderId, l.LineNumber }).IsUnique();
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SalesOrderLines_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_SalesOrderLines_Discount", "[DiscountPercent] BETWEEN 0 AND 100");
        });
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(i => i.Number).HasMaxLength(16).IsUnicode(false);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(i => i.SellerName).HasMaxLength(150);
        builder.Property(i => i.SellerGstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(i => i.SellerStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(i => i.SellerAddress).HasMaxLength(400);
        builder.Property(i => i.CustomerName).HasMaxLength(150);
        builder.Property(i => i.CustomerGstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(i => i.PlaceOfSupplyStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(i => i.PlaceOfSupplyStateName).HasMaxLength(60);
        builder.Property(i => i.CustomerReference).HasMaxLength(50);
        builder.Property(i => i.CancellationReason).HasMaxLength(500);
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.OwnsOne(i => i.BillingAddress, a => ConfigureAddress(a, "Billing"));
        builder.Navigation(i => i.BillingAddress).IsRequired();
        builder.OwnsOne(i => i.ShippingAddress, a => ConfigureAddress(a, "Shipping"));

        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => new { i.FinancialYear, i.InvoiceDate });
        builder.HasIndex(i => new { i.CustomerId, i.Status });
        builder.HasIndex(i => i.DueDate);
        // An order has at most one live invoice; cancelled ones stay in the register alongside it.
        builder.HasIndex(i => i.SalesOrderId).IsUnique().HasFilter("[Status] <> 'Cancelled'");

        builder.HasOne(i => i.SalesOrder).WithMany().HasForeignKey(i => i.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.CreatedBy).WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.Payments).WithOne(p => p.Invoice).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Invoices_AmountPaid", "[AmountPaid] >= 0 AND [AmountPaid] <= [GrandTotal]");
            t.HasCheckConstraint("CK_Invoices_DueDate", "[DueDate] >= [InvoiceDate]");
        });
    }

    private static void ConfigureAddress(OwnedNavigationBuilder<Invoice, InvoiceAddress> address, string prefix)
    {
        address.Property(a => a.Line1).HasColumnName($"{prefix}Line1").HasMaxLength(120);
        address.Property(a => a.Line2).HasColumnName($"{prefix}Line2").HasMaxLength(120);
        address.Property(a => a.City).HasColumnName($"{prefix}City").HasMaxLength(60);
        address.Property(a => a.StateCode).HasColumnName($"{prefix}StateCode").HasMaxLength(2).IsFixedLength().IsUnicode(false);
        address.Property(a => a.StateName).HasColumnName($"{prefix}StateName").HasMaxLength(60);
        address.Property(a => a.Pincode).HasColumnName($"{prefix}Pincode").HasMaxLength(6).IsUnicode(false);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.Property(l => l.ProductSku).HasMaxLength(40);
        builder.Property(l => l.ProductName).HasMaxLength(200);
        builder.Property(l => l.HsnCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        builder.Property(l => l.GstRate).HasPrecision(5, 2);
        builder.Property(l => l.CgstRate).HasPrecision(5, 2);
        builder.Property(l => l.SgstRate).HasPrecision(5, 2);
        builder.Property(l => l.IgstRate).HasPrecision(5, 2);
        builder.HasIndex(l => new { l.InvoiceId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.ProductId);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.Mode).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(p => p.Reference).HasMaxLength(50);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.HasIndex(p => p.PaymentDate);
        builder.HasOne(p => p.CreatedBy).WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0"));
    }
}

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.HasKey(s => new { s.DocumentType, s.FinancialYear });
        builder.Property(s => s.DocumentType).HasMaxLength(20).IsUnicode(false);
    }
}
