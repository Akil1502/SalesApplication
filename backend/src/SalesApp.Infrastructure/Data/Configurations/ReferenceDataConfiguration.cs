using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesApp.Core.Entities;

namespace SalesApp.Infrastructure.Data.Configurations;

internal sealed class StateConfiguration : IEntityTypeConfiguration<State>
{
    public void Configure(EntityTypeBuilder<State> builder)
    {
        builder.HasKey(s => s.Code);
        builder.Property(s => s.Code).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(s => s.Name).HasMaxLength(60).IsRequired();

        // GST state codes as used in GSTINs. Codes 25 (Daman & Diu, merged into 26 in 2020) and
        // 28 (pre-2014 Andhra Pradesh, now 37) are retired and deliberately left out.
        builder.HasData(
            S("01", "Jammu and Kashmir"),
            S("02", "Himachal Pradesh"),
            S("03", "Punjab"),
            S("04", "Chandigarh", utgst: true),
            S("05", "Uttarakhand"),
            S("06", "Haryana"),
            S("07", "Delhi"),
            S("08", "Rajasthan"),
            S("09", "Uttar Pradesh"),
            S("10", "Bihar"),
            S("11", "Sikkim"),
            S("12", "Arunachal Pradesh"),
            S("13", "Nagaland"),
            S("14", "Manipur"),
            S("15", "Mizoram"),
            S("16", "Tripura"),
            S("17", "Meghalaya"),
            S("18", "Assam"),
            S("19", "West Bengal"),
            S("20", "Jharkhand"),
            S("21", "Odisha"),
            S("22", "Chhattisgarh"),
            S("23", "Madhya Pradesh"),
            S("24", "Gujarat"),
            S("26", "Dadra and Nagar Haveli and Daman and Diu", utgst: true),
            S("27", "Maharashtra"),
            S("29", "Karnataka"),
            S("30", "Goa"),
            S("31", "Lakshadweep", utgst: true),
            S("32", "Kerala"),
            S("33", "Tamil Nadu"),
            S("34", "Puducherry"),
            S("35", "Andaman and Nicobar Islands", utgst: true),
            S("36", "Telangana"),
            S("37", "Andhra Pradesh"),
            S("38", "Ladakh", utgst: true),
            S("97", "Other Territory", utgst: true));
    }

    private static State S(string code, string name, bool utgst = false) => new() { Code = code, Name = name, UsesUtgst = utgst };
}

internal sealed class CompanyLogoConfiguration : IEntityTypeConfiguration<CompanyLogo>
{
    public void Configure(EntityTypeBuilder<CompanyLogo> builder)
    {
        builder.ToTable("CompanyLogo", t => t.HasCheckConstraint("CK_CompanyLogo_SingleRow", "[Id] = 1"));
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.ContentType).HasMaxLength(20).IsUnicode(false);
    }
}

internal sealed class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> builder)
    {
        builder.ToTable("CompanySettings", t => t.HasCheckConstraint("CK_CompanySettings_SingleRow", "[Id] = 1"));
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.LegalName).HasMaxLength(150);
        builder.Property(c => c.TradeName).HasMaxLength(150);
        builder.Property(c => c.Gstin).HasMaxLength(15).IsUnicode(false);
        builder.Property(c => c.StateCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.AddressLine1).HasMaxLength(120);
        builder.Property(c => c.AddressLine2).HasMaxLength(120);
        builder.Property(c => c.City).HasMaxLength(60);
        builder.Property(c => c.Pincode).HasMaxLength(6).IsUnicode(false);
        builder.Property(c => c.Phone).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.BankName).HasMaxLength(100);
        builder.Property(c => c.BankAccountNumber).HasMaxLength(18).IsUnicode(false);
        builder.Property(c => c.BankIfsc).HasMaxLength(11).IsUnicode(false);
        builder.Property(c => c.BankBranch).HasMaxLength(100);
        builder.Property(c => c.InvoicePrefix).HasMaxLength(5).IsUnicode(false);
        builder.Property(c => c.QuotationPrefix).HasMaxLength(5).IsUnicode(false);
        builder.Property(c => c.InvoiceTerms).HasMaxLength(2000);
        builder.Property(c => c.PurchaseOrderTerms).HasMaxLength(2000);
        builder.Property(c => c.OverReceiptTolerancePercent).HasPrecision(5, 2);
        builder.Property(c => c.BillPriceTolerancePercent).HasPrecision(5, 2);
        builder.Property(c => c.BrandColour).HasMaxLength(7).IsUnicode(false);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasOne(c => c.State).WithMany().HasForeignKey(c => c.StateCode).OnDelete(DeleteBehavior.Restrict);

        // A clearly fictitious demo company so invoices can be printed straight away. The PAN is the
        // standard documentation example (ABCDE1234F); the admin replaces all of this in Company settings.
        builder.HasData(new
        {
            Id = 1,
            LegalName = "Demo Textiles Private Limited",
            TradeName = "Demo Textiles",
            Gstin = "33ABCDE1234F1Z7",
            StateCode = "33",
            AddressLine1 = "12 Mill Road",
            City = "Coimbatore",
            Pincode = "641001",
            Email = "accounts@demotextiles.example",
            InvoicePrefix = "INV",
            QuotationPrefix = "QT",
            InvoiceTerms = "Goods once sold will not be taken back. Interest at 18% p.a. is charged on overdue bills. Subject to Coimbatore jurisdiction.",
            PurchaseOrderTerms = "Please quote our PO number on your invoice and delivery challan. Goods are accepted subject to inspection. Deliveries more than 5% over the ordered quantity will be returned.",
            PurchaseApprovalLimit = 500_000m,
            OverReceiptTolerancePercent = 5m,
            BillPriceTolerancePercent = 2m,
        });
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(80);
        builder.Property(c => c.Description).HasMaxLength(300);
        builder.HasIndex(c => c.Name).IsUnique();

        builder.HasData(
            new Category { Id = 1, Name = "Cotton yarn", Description = "Carded and combed cotton yarn, by count" },
            new Category { Id = 2, Name = "Synthetic & blended yarn", Description = "Polyester, viscose and blended yarn" },
            new Category { Id = 3, Name = "Fabrics", Description = "Woven and knitted fabrics" },
            new Category { Id = 4, Name = "Home textiles", Description = "Bed, table and kitchen linen" },
            new Category { Id = 5, Name = "Garments", Description = "Knitted and woven apparel" },
            new Category { Id = 6, Name = "Packing material", Description = "Cones, bags and cartons" });
    }
}

internal sealed class HsnCodeConfiguration : IEntityTypeConfiguration<HsnCode>
{
    public void Configure(EntityTypeBuilder<HsnCode> builder)
    {
        builder.Property(h => h.Code).HasMaxLength(8).IsUnicode(false);
        builder.Property(h => h.Description).HasMaxLength(300);
        builder.Property(h => h.GstRate).HasPrecision(5, 2);
        builder.HasIndex(h => h.Code).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_HsnCodes_GstRate", "[GstRate] >= 0 AND [GstRate] <= 40"));

        // Starter codes for the textile demo. Rates are illustrative: confirm current rates with
        // your tax advisor and edit them in the app; invoices copy the rate at the time of issue.
        builder.HasData(
            new HsnCode { Id = 1, Code = "5205", Description = "Cotton yarn (not sewing thread), 85% or more cotton, not for retail sale", GstRate = 5m },
            new HsnCode { Id = 2, Code = "5206", Description = "Cotton yarn (not sewing thread), less than 85% cotton, not for retail sale", GstRate = 5m },
            new HsnCode { Id = 3, Code = "5509", Description = "Yarn of synthetic staple fibres, not for retail sale", GstRate = 5m },
            new HsnCode { Id = 4, Code = "5208", Description = "Woven fabrics of cotton, 85% or more cotton, up to 200 g/m2", GstRate = 5m },
            new HsnCode { Id = 5, Code = "5407", Description = "Woven fabrics of synthetic filament yarn", GstRate = 5m },
            new HsnCode { Id = 6, Code = "6302", Description = "Bed linen, table linen, toilet and kitchen linen", GstRate = 5m },
            new HsnCode { Id = 7, Code = "6109", Description = "T-shirts, singlets and vests, knitted or crocheted", GstRate = 5m },
            new HsnCode { Id = 8, Code = "3923", Description = "Plastic articles for packing goods (cones, bobbins, bags)", GstRate = 18m });
    }
}
