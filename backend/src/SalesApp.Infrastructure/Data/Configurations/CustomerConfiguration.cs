using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <summary>Database sequence behind generated customer codes (C00001, C00002, ...).</summary>
    public const string CodeSequence = "CustomerNumbers";

    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Code).HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(150);
        builder.Property(c => c.TradeName).HasMaxLength(150);
        builder.Property(c => c.Gstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(c => c.ContactPerson).HasMaxLength(100);
        builder.Property(c => c.Phone).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.OwnsOne(c => c.BillingAddress, address => ConfigureAddress(address, "Billing"));
        builder.Navigation(c => c.BillingAddress).IsRequired();
        builder.OwnsOne(c => c.ShippingAddress, address => ConfigureAddress(address, "Shipping"));

        builder.HasOne(c => c.AssignedRep).WithMany().HasForeignKey(c => c.AssignedRepId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.Gstin).HasFilter("[Gstin] IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Customers_CreditLimit", "[CreditLimit] IS NULL OR [CreditLimit] >= 0");
            t.HasCheckConstraint("CK_Customers_PaymentTerms", "[PaymentTermsDays] BETWEEN 0 AND 365");
        });
    }

    private static void ConfigureAddress<TOwner>(OwnedNavigationBuilder<TOwner, Address> address, string prefix)
        where TOwner : class
    {
        // Flat column names (BillingCity, ShippingPincode) read better than EF's BillingAddress_City.
        address.Property(a => a.Line1).HasColumnName($"{prefix}Line1").HasMaxLength(120);
        address.Property(a => a.Line2).HasColumnName($"{prefix}Line2").HasMaxLength(120);
        address.Property(a => a.City).HasColumnName($"{prefix}City").HasMaxLength(60);
        address.Property(a => a.StateCode).HasColumnName($"{prefix}StateCode").HasMaxLength(2).IsFixedLength().IsUnicode(false);
        address.Property(a => a.Pincode).HasColumnName($"{prefix}Pincode").HasMaxLength(6).IsUnicode(false);
        address.HasOne<State>().WithMany().HasForeignKey(a => a.StateCode).OnDelete(DeleteBehavior.Restrict);
    }
}
