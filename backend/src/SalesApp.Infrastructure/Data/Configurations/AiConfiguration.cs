using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class AiUsageLogConfiguration : IEntityTypeConfiguration<AiUsageLog>
{
    public void Configure(EntityTypeBuilder<AiUsageLog> builder)
    {
        builder.Property(l => l.Feature).HasMaxLength(40).IsUnicode(false);
        builder.Property(l => l.Model).HasMaxLength(60).IsUnicode(false);
        builder.Property(l => l.CostUsd).HasPrecision(18, 6);
        builder.Property(l => l.Outcome).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(l => l.Error).HasMaxLength(500);
        builder.Property(l => l.RequestId).HasMaxLength(100).IsUnicode(false);

        builder.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Restrict);

        // The daily budget check sums one user's recent calls; the usage page lists recent calls for everyone.
        builder.HasIndex(l => new { l.UserId, l.CreatedAtUtc });
        builder.HasIndex(l => l.CreatedAtUtc);
    }
}

internal sealed class ProductAliasConfiguration : IEntityTypeConfiguration<ProductAlias>
{
    public void Configure(EntityTypeBuilder<ProductAlias> builder)
    {
        builder.Property(a => a.Alias).HasMaxLength(120);
        builder.Property(a => a.NormalizedAlias).HasMaxLength(120);
        builder.HasIndex(a => a.NormalizedAlias).IsUnique();

        builder.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.CreatedBy).WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AiQueryLogConfiguration : IEntityTypeConfiguration<AiQueryLog>
{
    public void Configure(EntityTypeBuilder<AiQueryLog> builder)
    {
        builder.Property(l => l.Question).HasMaxLength(1000);
        builder.Property(l => l.RunAs).HasMaxLength(30).IsUnicode(false);
        builder.Property(l => l.Outcome).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(l => l.Error).HasMaxLength(1000);

        builder.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.CreatedAtUtc);
        builder.HasIndex(l => l.RunId);
    }
}
