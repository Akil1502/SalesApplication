using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data;

/// <summary>
/// The single EF Core context for the sales database. Identity tables keep their standard
/// AspNet* names so any Identity tooling or documentation applies unchanged.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.FullName).HasMaxLength(120).IsRequired();
            user.Property(u => u.CreatedAtUtc).HasColumnType("datetime2(0)");
        });

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is decimal(18,2) unless a property says otherwise (quantities use decimal(18,3)).
        // Setting it globally means no decimal column silently falls back to EF's default precision.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
