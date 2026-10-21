using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
        builder.Property(n => n.Title).HasMaxLength(200);
        builder.Property(n => n.Body).HasMaxLength(400);
        builder.Property(n => n.Link).HasMaxLength(200).IsUnicode(false);
        builder.Property(n => n.DedupKey).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(n => n.CreatedAtUtc);
        builder.HasIndex(n => n.RecipientUserId);
        builder.HasIndex(n => n.DedupKey).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
    }
}

internal sealed class UserNotificationStateConfiguration : IEntityTypeConfiguration<UserNotificationState>
{
    public void Configure(EntityTypeBuilder<UserNotificationState> builder)
    {
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.MutedKinds).HasMaxLength(400).IsUnicode(false);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> builder)
    {
        builder.HasKey(r => new { r.UserId, r.NotificationId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Notification>().WithMany().HasForeignKey(r => r.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}
