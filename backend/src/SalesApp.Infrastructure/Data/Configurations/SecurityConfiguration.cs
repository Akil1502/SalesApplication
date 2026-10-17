using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.Property(r => r.Description).HasMaxLength(200);
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoleGrantConfiguration : IEntityTypeConfiguration<RoleGrant>
{
    public void Configure(EntityTypeBuilder<RoleGrant> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.Permission });
        builder.Property(p => p.Permission).HasMaxLength(50).IsUnicode(false);
    }
}

internal sealed class UserPermissionChangeConfiguration : IEntityTypeConfiguration<UserPermissionChange>
{
    public void Configure(EntityTypeBuilder<UserPermissionChange> builder)
    {
        builder.ToTable("UserPermissions");
        builder.HasKey(p => new { p.UserId, p.Permission });
        builder.Property(p => p.Permission).HasMaxLength(50).IsUnicode(false);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserSettingConfiguration : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> builder)
    {
        builder.HasKey(s => new { s.UserId, s.Key });
        builder.Property(s => s.Key).HasMaxLength(64).IsUnicode(false);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_UserSettings_Size", "LEN([ValueJson]) <= 32768"));
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
        builder.Property(a => a.EntityType).HasMaxLength(40);
        builder.Property(a => a.EntityId).HasMaxLength(64).IsUnicode(false);
        builder.Property(a => a.EntityLabel).HasMaxLength(300);
        builder.Property(a => a.Summary).HasMaxLength(1000);
        builder.Property(a => a.IpAddress).HasMaxLength(45).IsUnicode(false);

        // A record's history, and the newest entries first.
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.Id });
        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => a.UserId);
    }
}
