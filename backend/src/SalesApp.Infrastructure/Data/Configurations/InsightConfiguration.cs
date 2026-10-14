using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class InsightRunConfiguration : IEntityTypeConfiguration<InsightRun>
{
    public void Configure(EntityTypeBuilder<InsightRun> builder)
    {
        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.NarrativeStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.NarrativeNote).HasMaxLength(1000);
        builder.Property(r => r.CostUsd).HasPrecision(18, 6);

        builder.HasOne(r => r.RequestedBy).WithMany().HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);

        // The page shows the latest run; the nightly job checks whether today's has happened.
        builder.HasIndex(r => r.CreatedAtUtc);
    }
}
