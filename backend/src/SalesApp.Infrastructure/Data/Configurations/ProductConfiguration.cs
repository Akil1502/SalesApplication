using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Sku).HasMaxLength(40);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);

        // Stored as text ("Kg", "Metre") so the column reads naturally in SQL and in AI-generated queries.
        builder.Property(p => p.Unit).HasConversion<string>().HasMaxLength(10).IsUnicode(false);

        // Quantities carry three decimals (kg to the gram, metres to the millimetre).
        builder.Property(p => p.ReorderLevel).HasPrecision(18, 3);
        builder.Property(p => p.ReorderQuantity).HasPrecision(18, 3);

        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Name);

        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.HsnCode).WithMany().HasForeignKey(p => p.HsnCodeId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_ListPrice", "[ListPrice] >= 0");
            t.HasCheckConstraint("CK_Products_CostPrice", "[CostPrice] IS NULL OR [CostPrice] >= 0");
        });
    }
}
