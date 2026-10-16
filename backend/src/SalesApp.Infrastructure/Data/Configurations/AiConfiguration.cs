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

internal sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.Property(c => c.Title).HasMaxLength(120);
        builder.Property(c => c.CostUsd).HasPrecision(18, 6);

        builder.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);

        // The history list shows one user's most recent conversations.
        builder.HasIndex(c => new { c.UserId, c.UpdatedAtUtc });
    }
}

internal sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public const string AppendOnlyTrigger = "TR_AiMessages_AppendOnly";

    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        // The trigger (Sql/003_assistant.sql) rejects updates. Declaring it makes EF write inserts without the
        // OUTPUT clause, which SQL Server doesn't allow on tables with triggers.
        builder.ToTable(t => t.HasTrigger(AppendOnlyTrigger));

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);

        builder.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();
    }
}

internal sealed class AiDraftConfiguration : IEntityTypeConfiguration<AiDraft>
{
    public void Configure(EntityTypeBuilder<AiDraft> builder)
    {
        builder.Property(d => d.ToolUseId).HasMaxLength(100).IsUnicode(false);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(d => d.Purpose).HasMaxLength(40).IsUnicode(false);
        builder.Property(d => d.Subject).HasMaxLength(200);

        builder.HasOne(d => d.Conversation).WithMany().HasForeignKey(d => d.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Supplier).WithMany().HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.ConversationId, d.ToolUseId }).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_AiDrafts_Recipient", "([CustomerId] IS NOT NULL AND [SupplierId] IS NULL) OR ([CustomerId] IS NULL AND [SupplierId] IS NOT NULL)"));
    }
}
