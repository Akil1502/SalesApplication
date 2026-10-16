using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.HasKey(s => s.ProductId);
        builder.Property(s => s.QuantityOnHand).HasPrecision(18, 3);
        builder.HasOne(s => s.Product).WithOne(p => p.Stock).HasForeignKey<StockLevel>(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(m => m.Reason).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(m => m.Quantity).HasPrecision(18, 3);
        builder.Property(m => m.BalanceAfter).HasPrecision(18, 3);
        builder.Property(m => m.ReferenceType).HasMaxLength(20).IsUnicode(false);
        builder.Property(m => m.ReferenceNumber).HasMaxLength(50);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.CreatedBy).WithMany().HasForeignKey(m => m.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        // The ledger is read per product in date order, and by reference (all lines of a receipt or invoice).
        builder.HasIndex(m => new { m.ProductId, m.MovementDate, m.Id });
        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId });

        builder.ToTable(t => t.HasCheckConstraint("CK_StockMovements_NonZero", "[Quantity] <> 0"));
    }
}

internal sealed class StockReceiptConfiguration : IEntityTypeConfiguration<StockReceipt>
{
    public const string NumberSequence = "StockReceiptNumbers";

    public void Configure(EntityTypeBuilder<StockReceipt> builder)
    {
        builder.Property(r => r.Number).HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.SupplierName).HasMaxLength(150);
        builder.Property(r => r.SupplierReference).HasMaxLength(50);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.HasIndex(r => r.Number).IsUnique();
        builder.HasIndex(r => r.ReceiptDate);

        builder.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.PurchaseOrder).WithMany().HasForeignKey(r => r.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.StockReceiptId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StockReceiptLineConfiguration : IEntityTypeConfiguration<StockReceiptLine>
{
    public void Configure(EntityTypeBuilder<StockReceiptLine> builder)
    {
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.QuantityRejected).HasPrecision(18, 3);
        builder.Property(l => l.RejectionReason).HasMaxLength(200);
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(l => l.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.PurchaseOrderLineId);
        // A line can be all rejected (nothing accepted), but it must record something.
        builder.ToTable(t => t.HasCheckConstraint("CK_StockReceiptLines_Quantity", "[Quantity] >= 0 AND [QuantityRejected] >= 0 AND [Quantity] + [QuantityRejected] > 0"));
    }
}
