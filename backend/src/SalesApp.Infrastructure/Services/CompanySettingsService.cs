using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ICompanySettingsService
{
    Task<CompanySettingsDto> GetAsync(CancellationToken cancellationToken);
    Task<CompanySettingsDto> UpdateAsync(CompanySettingsSaveRequest request, CancellationToken cancellationToken);
}

public sealed class CompanySettingsService(AppDbContext db, ILookupService lookups) : ICompanySettingsService
{
    private const int SingletonId = 1;

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

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(settings, cancellationToken);
    }

    private async Task<CompanySettingsDto> ToDtoAsync(CompanySettings s, CancellationToken cancellationToken)
    {
        var states = await lookups.GetStatesAsync(cancellationToken);
        return new CompanySettingsDto(
            s.LegalName, s.TradeName, s.Gstin, s.StateCode, states.GetValueOrDefault(s.StateCode)?.Name ?? s.StateCode,
            s.AddressLine1, s.AddressLine2, s.City, s.Pincode, s.Phone, s.Email,
            s.BankName, s.BankAccountNumber, s.BankIfsc, s.BankBranch,
            s.InvoicePrefix, s.QuotationPrefix, s.InvoiceTerms, s.PurchaseOrderTerms, s.PurchaseApprovalLimit,
            s.OverReceiptTolerancePercent, s.BillPriceTolerancePercent, RowVersions.ToToken(s.RowVersion));
    }
}
