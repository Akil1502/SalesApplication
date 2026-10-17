using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ICompanySettingsService
{
    Task<CompanySettingsDto> GetAsync(CancellationToken cancellationToken);
    Task<CompanySettingsDto> UpdateAsync(CompanySettingsSaveRequest request, CancellationToken cancellationToken);

    /// <summary>The name, colour and logo, for the sign-in page (before anyone has signed in).</summary>
    Task<BrandingDto> GetBrandingAsync(CancellationToken cancellationToken);

    /// <summary>Sets the logo. The browser has already shrunk it, so it's stored as it comes.</summary>
    Task<BrandingDto> SaveLogoAsync(byte[] image, CancellationToken cancellationToken);

    Task<BrandingDto> DeleteLogoAsync(CancellationToken cancellationToken);

    /// <summary>The logo, or null when there's none at that version (it has been replaced or removed).</summary>
    Task<ImageFile?> ReadLogoAsync(int version, CancellationToken cancellationToken);
}

public sealed class CompanySettingsService(AppDbContext db, ILookupService lookups, IAuditLog audit) : ICompanySettingsService
{
    private const int SingletonId = 1;

    public const int MaxLogoBytes = 512 * 1024;

    public async Task<CompanySettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(s => s.Id == SingletonId, cancellationToken);
        return await ToDtoAsync(settings, cancellationToken);
    }

    public async Task<CompanySettingsDto> UpdateAsync(CompanySettingsSaveRequest request, CancellationToken cancellationToken)
    {
        var settings = await db.CompanySettings.SingleAsync(s => s.Id == SingletonId, cancellationToken);
        db.Entry(settings).Property(s => s.RowVersion).OriginalValue = RowVersions.Parse(request.RowVersion);

        var state = await lookups.GetStateOrThrowAsync(request.StateCode, "stateCode", cancellationToken);
        var gstin = Gstin.Normalize(request.Gstin);
        if (Gstin.StateCode(gstin) != state.Code)
        {
            throw new ValidationException("gstin", $"This GSTIN is registered in state code {Gstin.StateCode(gstin)}, but the address is in {state.Name} ({state.Code}).");
        }

        settings.LegalName = request.LegalName.Trim();
        settings.TradeName = Text.NullIfBlank(request.TradeName);
        settings.Gstin = gstin;
        settings.StateCode = state.Code;
        settings.AddressLine1 = request.AddressLine1.Trim();
        settings.AddressLine2 = Text.NullIfBlank(request.AddressLine2);
        settings.City = request.City.Trim();
        settings.Pincode = request.Pincode;
        settings.Phone = Text.NormalizePhone(request.Phone);
        settings.Email = Text.NullIfBlank(request.Email);
        settings.BankName = Text.NullIfBlank(request.BankName);
        settings.BankAccountNumber = Text.NullIfBlank(request.BankAccountNumber);
        settings.BankIfsc = Text.NullIfBlank(request.BankIfsc);
        settings.BankBranch = Text.NullIfBlank(request.BankBranch);
        settings.InvoicePrefix = request.InvoicePrefix;
        settings.QuotationPrefix = request.QuotationPrefix;
        settings.InvoiceTerms = Text.NullIfBlank(request.InvoiceTerms);
        settings.PurchaseOrderTerms = Text.NullIfBlank(request.PurchaseOrderTerms);
        settings.PurchaseApprovalLimit = request.PurchaseApprovalLimit;
        settings.OverReceiptTolerancePercent = request.OverReceiptTolerancePercent;
        settings.BillPriceTolerancePercent = request.BillPriceTolerancePercent;
        settings.BrandColour = Text.NullIfBlank(request.BrandColour)?.ToLowerInvariant();

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(settings, cancellationToken);
    }

    public async Task<BrandingDto> GetBrandingAsync(CancellationToken cancellationToken)
    {
        var settings = await db.CompanySettings.AsNoTracking()
            .Where(s => s.Id == SingletonId)
            .Select(s => new { s.LegalName, s.TradeName, s.BrandColour })
            .SingleAsync(cancellationToken);
        return new BrandingDto(settings.TradeName ?? settings.LegalName, settings.BrandColour, await LogoUrlAsync(cancellationToken));
    }

    public async Task<BrandingDto> SaveLogoAsync(byte[] image, CancellationToken cancellationToken)
    {
        if (image.Length == 0)
        {
            throw new ValidationException("logo", "Choose a picture.");
        }

        if (image.Length > MaxLogoBytes)
        {
            throw new ValidationException("logo", string.Create(CultureInfo.InvariantCulture, $"The logo is too large; it can be at most {MaxLogoBytes / 1024} KB."));
        }

        var contentType = ImageFiles.Detect(image) ?? throw new ValidationException("logo", "Choose a PNG, JPEG or WebP picture.");
        await ChangeLogoAsync(hadLogo => hadLogo ? ("Earlier logo", "New logo") : (null, "Added"), logo =>
        {
            logo.ContentType = contentType;
            logo.ImageData = image;
        }, cancellationToken);
        return await GetBrandingAsync(cancellationToken);
    }

    public async Task<BrandingDto> DeleteLogoAsync(CancellationToken cancellationToken)
    {
        await ChangeLogoAsync(hadLogo => hadLogo ? ("Logo", "Removed") : throw new NotFoundException("There's no logo to remove."), logo =>
        {
            logo.ContentType = null;
            logo.ImageData = null;
        }, cancellationToken);
        return await GetBrandingAsync(cancellationToken);
    }

    public async Task<ImageFile?> ReadLogoAsync(int version, CancellationToken cancellationToken) =>
        await db.CompanyLogos.AsNoTracking()
            .Where(l => l.Id == SingletonId && l.Version == version && l.ImageData != null)
            .Select(l => new ImageFile(l.ImageData!, l.ContentType!))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Changes the logo under a lock and records it; describe gives the history entry from whether there was a logo, or throws to refuse.</summary>
    private async Task ChangeLogoAsync(Func<bool, (string? Before, string After)> describe, Action<CompanyLogo> change, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Locked (the missing row's key too), so two changes at once can't both become the same version.
        var logo = await db.CompanyLogos
            .FromSql($"SELECT * FROM CompanyLogo WITH (UPDLOCK, HOLDLOCK) WHERE Id = {SingletonId}")
            .SingleOrDefaultAsync(cancellationToken);
        var (before, after) = describe(logo?.ImageData is not null);
        if (logo is null)
        {
            logo = new CompanyLogo { Id = SingletonId };
            db.CompanyLogos.Add(logo);
        }

        change(logo);
        logo.Version++;
        logo.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await RecordLogoChangeAsync(before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<string?> LogoUrlAsync(CancellationToken cancellationToken)
    {
        var version = await db.CompanyLogos.AsNoTracking()
            .Where(l => l.Id == SingletonId && l.ImageData != null)
            .Select(l => (int?)l.Version)
            .SingleOrDefaultAsync(cancellationToken);
        return version is { } v ? string.Create(CultureInfo.InvariantCulture, $"/api/branding/logo?v={v}") : null;
    }

    /// <summary>A logo change, in the company settings' history (the picture itself is never copied into the trail).</summary>
    private async Task RecordLogoChangeAsync(string? before, string after, CancellationToken cancellationToken)
    {
        var label = await db.CompanySettings.Where(s => s.Id == SingletonId).Select(s => s.LegalName).SingleAsync(cancellationToken);
        await audit.RecordAsync(new AuditEvent(
            AuditAction.Updated, "Company settings", SingletonId.ToString(CultureInfo.InvariantCulture), label,
            Changes: [new AuditChange("Logo", before, after)]), cancellationToken);
    }

    private async Task<CompanySettingsDto> ToDtoAsync(CompanySettings s, CancellationToken cancellationToken)
    {
        var states = await lookups.GetStatesAsync(cancellationToken);
        var logoUrl = await LogoUrlAsync(cancellationToken);
        return new CompanySettingsDto(
            s.LegalName, s.TradeName, s.Gstin, s.StateCode, states.GetValueOrDefault(s.StateCode)?.Name ?? s.StateCode,
            s.AddressLine1, s.AddressLine2, s.City, s.Pincode, s.Phone, s.Email,
            s.BankName, s.BankAccountNumber, s.BankIfsc, s.BankBranch,
            s.InvoicePrefix, s.QuotationPrefix, s.InvoiceTerms, s.PurchaseOrderTerms, s.PurchaseApprovalLimit,
            s.OverReceiptTolerancePercent, s.BillPriceTolerancePercent, s.BrandColour, logoUrl, RowVersions.ToToken(s.RowVersion));
    }
}
