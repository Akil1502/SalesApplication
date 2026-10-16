using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.Property(c => c.Number).HasMaxLength(16).IsUnicode(false);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Reason).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.ReasonNote).HasMaxLength(500);
        builder.Property(c => c.SellerName).HasMaxLength(150);
        builder.Property(c => c.SellerGstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(c => c.SellerStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.SellerAddress).HasMaxLength(400);
        builder.Property(c => c.CustomerName).HasMaxLength(150);
        builder.Property(c => c.CustomerGstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(c => c.PlaceOfSupplyStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.PlaceOfSupplyStateName).HasMaxLength(60);
        builder.Property(c => c.CancellationReason).HasMaxLength(500);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.OwnsOne(c => c.BillingAddress, a =>
        {
            a.Property(x => x.Line1).HasColumnName("BillingLine1").HasMaxLength(120);
            a.Property(x => x.Line2).HasColumnName("BillingLine2").HasMaxLength(120);
            a.Property(x => x.City).HasColumnName("BillingCity").HasMaxLength(60);
            a.Property(x => x.StateCode).HasColumnName("BillingStateCode").HasMaxLength(2).IsFixedLength().IsUnicode(false);
            a.Property(x => x.StateName).HasColumnName("BillingStateName").HasMaxLength(60);
            a.Property(x => x.Pincode).HasColumnName("BillingPincode").HasMaxLength(6).IsUnicode(false);
        });
        builder.Navigation(c => c.BillingAddress).IsRequired();

        builder.HasIndex(c => c.Number).IsUnique();
        builder.HasIndex(c => new { c.FinancialYear, c.CreditNoteDate });
        builder.HasIndex(c => c.InvoiceId);
        builder.HasIndex(c => new { c.CustomerId, c.Status });

        builder.HasOne(c => c.Invoice).WithMany().HasForeignKey(c => c.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Lines).WithOne().HasForeignKey(l => l.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Adjustments).WithOne(a => a.CreditNote).HasForeignKey(a => a.CreditNoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Refunds).WithOne(r => r.CreditNote).HasForeignKey(r => r.CreditNoteId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_CreditNotes_Used",
                "[AmountAdjusted] >= 0 AND [AmountRefunded] >= 0 AND [AmountAdjusted] + [AmountRefunded] <= [GrandTotal]");
            t.HasCheckConstraint("CK_CreditNotes_Total", "[GrandTotal] > 0");
        });
    }
}

internal sealed class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        builder.Property(l => l.ProductSku).HasMaxLength(40);
        builder.Property(l => l.ProductName).HasMaxLength(200);
        builder.Property(l => l.HsnCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.Disposition).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(l => l.GstRate).HasPrecision(5, 2);
        builder.HasIndex(l => new { l.CreditNoteId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.InvoiceLineId);
        builder.HasIndex(l => l.ProductId);
        builder.HasOne<InvoiceLine>().WithMany().HasForeignKey(l => l.InvoiceLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_CreditNoteLines_Value", "[TaxableValue] > 0");
            t.HasCheckConstraint("CK_CreditNoteLines_Goods",
                "([Quantity] IS NULL AND [Disposition] IS NULL) OR ([Quantity] > 0 AND [Disposition] IS NOT NULL)");
        });
    }
}

internal sealed class CreditAdjustmentConfiguration : IEntityTypeConfiguration<CreditAdjustment>
{
    public void Configure(EntityTypeBuilder<CreditAdjustment> builder)
    {
        builder.HasIndex(a => a.InvoiceId);
        builder.HasOne(a => a.Invoice).WithMany().HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.CreatedBy).WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_CreditAdjustments_Amount", "[Amount] > 0"));
    }
}

internal sealed class CreditRefundConfiguration : IEntityTypeConfiguration<CreditRefund>
{
    public void Configure(EntityTypeBuilder<CreditRefund> builder)
    {
        builder.Property(r => r.Mode).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(r => r.Reference).HasMaxLength(50);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.HasIndex(r => r.RefundDate);
        builder.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_CreditRefunds_Amount", "[Amount] > 0"));
    }
}

internal sealed class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.Property(r => r.Number).HasMaxLength(16).IsUnicode(false);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.ReasonNote).HasMaxLength(500);
        builder.Property(r => r.SupplierName).HasMaxLength(150);
        builder.Property(r => r.SupplierGstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(r => r.SupplierStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.SupplierCreditNoteNumber).HasMaxLength(30);
        builder.Property(r => r.CancellationReason).HasMaxLength(500);
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.Number).IsUnique();
        builder.HasIndex(r => new { r.FinancialYear, r.ReturnDate });
        builder.HasIndex(r => r.SupplierBillId);
        builder.HasIndex(r => new { r.SupplierId, r.Status });

        builder.HasOne(r => r.SupplierBill).WithMany().HasForeignKey(r => r.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<State>().WithMany().HasForeignKey(r => r.SupplierStateCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.SupplierReturnId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Adjustments).WithOne(a => a.SupplierReturn).HasForeignKey(a => a.SupplierReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Refunds).WithOne(f => f.SupplierReturn).HasForeignKey(f => f.SupplierReturnId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SupplierReturns_Used",
                "[AmountAdjusted] >= 0 AND [AmountRefunded] >= 0 AND [AmountAdjusted] + [AmountRefunded] <= [GrandTotal]");
            t.HasCheckConstraint("CK_SupplierReturns_Total", "[GrandTotal] > 0");
        });
    }
}

internal sealed class SupplierReturnLineConfiguration : IEntityTypeConfiguration<SupplierReturnLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnLine> builder)
    {
        builder.Property(l => l.ProductSku).HasMaxLength(40);
        builder.Property(l => l.ProductName).HasMaxLength(200);
        builder.Property(l => l.HsnCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.GstRate).HasPrecision(5, 2);
        builder.HasIndex(l => new { l.SupplierReturnId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.SupplierBillLineId);
        builder.HasIndex(l => l.ProductId);
        builder.HasOne<SupplierBillLine>().WithMany().HasForeignKey(l => l.SupplierBillLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierReturnLines_Quantity", "[Quantity] > 0 AND [TaxableValue] > 0"));
    }
}

internal sealed class SupplierCreditAdjustmentConfiguration : IEntityTypeConfiguration<SupplierCreditAdjustment>
{
    public void Configure(EntityTypeBuilder<SupplierCreditAdjustment> builder)
    {
        builder.HasIndex(a => a.SupplierBillId);
        builder.HasOne(a => a.SupplierBill).WithMany().HasForeignKey(a => a.SupplierBillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.CreatedBy).WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierCreditAdjustments_Amount", "[Amount] > 0"));
    }
}

internal sealed class SupplierRefundConfiguration : IEntityTypeConfiguration<SupplierRefund>
{
    public void Configure(EntityTypeBuilder<SupplierRefund> builder)
    {
        builder.Property(r => r.Mode).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(r => r.Reference).HasMaxLength(50);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.HasIndex(r => r.RefundDate);
        builder.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierRefunds_Amount", "[Amount] > 0"));
    }
}
