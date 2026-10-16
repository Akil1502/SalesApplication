using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    /// <summary>Database sequence behind generated supplier codes (S00001, S00002, ...).</summary>
    public const string CodeSequence = "SupplierNumbers";

    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.Code).HasMaxLength(20);
        builder.Property(s => s.Name).HasMaxLength(150);
        builder.Property(s => s.TradeName).HasMaxLength(150);
        builder.Property(s => s.Gstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(s => s.ContactPerson).HasMaxLength(100);
        builder.Property(s => s.Phone).HasMaxLength(20).IsUnicode(false);
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.Property(s => s.BankName).HasMaxLength(100);
        builder.Property(s => s.BankAccountNumber).HasMaxLength(18).IsUnicode(false);
        builder.Property(s => s.BankIfsc).HasMaxLength(11).IsUnicode(false);
        builder.Property(s => s.Notes).HasMaxLength(2000);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.OwnsOne(s => s.Address, address =>
        {
            address.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(120);
            address.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(120);
            address.Property(a => a.City).HasColumnName("City").HasMaxLength(60);
            address.Property(a => a.StateCode).HasColumnName("StateCode").HasMaxLength(2).IsFixedLength().IsUnicode(false);
            address.Property(a => a.Pincode).HasColumnName("Pincode").HasMaxLength(6).IsUnicode(false);
            address.HasOne<State>().WithMany().HasForeignKey(a => a.StateCode).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(s => s.Address).IsRequired();

        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.Name);
        builder.HasIndex(s => s.Gstin).HasFilter("[Gstin] IS NOT NULL");

        builder.ToTable(t => t.HasCheckConstraint("CK_Suppliers_PaymentTerms", "[PaymentTermsDays] BETWEEN 0 AND 365"));
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Property(o => o.Number).HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.SupplierReference).HasMaxLength(50);
        builder.Property(o => o.Notes).HasMaxLength(2000);
        builder.Property(o => o.RejectionReason).HasMaxLength(500);
        builder.Property(o => o.CloseReason).HasMaxLength(500);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => new { o.SupplierId, o.Status });
        builder.HasIndex(o => o.OrderDate);

        builder.HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.CreatedBy).WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.ApprovedBy).WithMany().HasForeignKey(o => o.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("CK_PurchaseOrders_ExpectedDate", "[ExpectedDate] IS NULL OR [ExpectedDate] >= [OrderDate]"));
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.QuantityReceived).HasPrecision(18, 3);
        builder.Property(l => l.QuantityBilled).HasPrecision(18, 3);
        builder.Property(l => l.GstRate).HasPrecision(5, 2);
        builder.HasIndex(l => new { l.PurchaseOrderId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.ProductId);
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PurchaseOrderLines_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_PurchaseOrderLines_Received", "[QuantityReceived] >= 0 AND [QuantityBilled] >= 0");
        });
    }
}

internal sealed class SupplierBillConfiguration : IEntityTypeConfiguration<SupplierBill>
{
    public void Configure(EntityTypeBuilder<SupplierBill> builder)
    {
        builder.Property(b => b.Number).HasMaxLength(16).IsUnicode(false);
        builder.Property(b => b.SupplierInvoiceNumber).HasMaxLength(30);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(b => b.MatchStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(b => b.Source).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(b => b.SupplierName).HasMaxLength(150);
        builder.Property(b => b.SupplierGstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(b => b.SupplierStateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(b => b.ChargesGstRate).HasPrecision(5, 2);
        builder.Property(b => b.ChargesDescription).HasMaxLength(100);
        builder.Property(b => b.ApprovalNote).HasMaxLength(500);
        builder.Property(b => b.Notes).HasMaxLength(1000);
        builder.Property(b => b.CancellationReason).HasMaxLength(500);
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasIndex(b => b.Number).IsUnique();
        // A supplier's invoice is entered once; a cancelled entry doesn't block entering it again correctly.
        builder.HasIndex(b => new { b.SupplierId, b.SupplierInvoiceNumber }).IsUnique().HasFilter("[Status] <> 'Cancelled'");
        builder.HasIndex(b => new { b.FinancialYear, b.BillDate });
        builder.HasIndex(b => b.DueDate);
        builder.HasIndex(b => b.PurchaseOrderId);

        builder.HasOne(b => b.Supplier).WithMany().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.PurchaseOrder).WithMany().HasForeignKey(b => b.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.CreatedBy).WithMany().HasForeignKey(b => b.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.ApprovedBy).WithMany().HasForeignKey(b => b.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<State>().WithMany().HasForeignKey(b => b.SupplierStateCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.SupplierBillId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Payments).WithOne(p => p.SupplierBill).HasForeignKey(p => p.SupplierBillId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SupplierBills_AmountPaid", "[AmountPaid] >= 0 AND [AmountPaid] <= [GrandTotal]");
            t.HasCheckConstraint("CK_SupplierBills_DueDate", "[DueDate] >= [BillDate]");
            t.HasCheckConstraint("CK_SupplierBills_Charges", "[ChargesAmount] >= 0");
        });
    }
}

internal sealed class SupplierBillLineConfiguration : IEntityTypeConfiguration<SupplierBillLine>
{
    public void Configure(EntityTypeBuilder<SupplierBillLine> builder)
    {
        builder.Property(l => l.ProductSku).HasMaxLength(40);
        builder.Property(l => l.ProductName).HasMaxLength(200);
        builder.Property(l => l.HsnCode).HasMaxLength(8).IsUnicode(false);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.GstRate).HasPrecision(5, 2);
        builder.Property(l => l.CgstRate).HasPrecision(5, 2);
        builder.Property(l => l.SgstRate).HasPrecision(5, 2);
        builder.Property(l => l.IgstRate).HasPrecision(5, 2);
        builder.Property(l => l.Issue).HasMaxLength(500);
        builder.HasIndex(l => new { l.SupplierBillId, l.LineNumber }).IsUnique();
        builder.HasIndex(l => l.PurchaseOrderLineId);
        builder.HasIndex(l => l.ProductId);
        builder.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(l => l.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierBillLines_Quantity", "[Quantity] > 0"));
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.Property(p => p.Mode).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(p => p.Reference).HasMaxLength(50);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.HasIndex(p => p.PaymentDate);
        builder.HasOne(p => p.CreatedBy).WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_SupplierPayments_Amount", "[Amount] > 0"));
    }
}
