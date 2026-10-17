using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Company settings: everyone can read them (they appear on documents); only admins change them.</summary>
public sealed class SettingsController(ICompanySettingsService settings) : ApiControllerBase
{
    [HttpGet("company")]
    [ProducesResponseType<CompanySettingsDto>(StatusCodes.Status200OK)]
    public Task<CompanySettingsDto> GetCompany(CancellationToken cancellationToken) => settings.GetAsync(cancellationToken);

    [HttpPut("company")]
    [RequirePermission(Permissions.SettingsEdit)]
    [ProducesResponseType<CompanySettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CompanySettingsDto> UpdateCompany(CompanySettingsSaveRequest request, CancellationToken cancellationToken) =>
        settings.UpdateAsync(request, cancellationToken);

    /// <summary>
    /// Sets the company logo (menu, sign-in page, browser tab, printed documents). The browser has already shrunk it
    /// to at most 512 px; PNG keeps a transparent background. SVG isn't accepted, as it can carry script.
    /// </summary>
    [HttpPut("company/logo")]
    [RequirePermission(Permissions.SettingsEdit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxLogoUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxLogoUploadBytes)]
    [ProducesResponseType<BrandingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    public async Task<BrandingDto> SetLogo([FromForm] CompanyLogoForm form, CancellationToken cancellationToken)
    {
        if (form.Logo is not { Length: > 0 } upload)
        {
            throw new ValidationException("logo", "Choose a picture.");
        }

        if (upload.Length > CompanySettingsService.MaxLogoBytes)
        {
            throw new ValidationException("logo", string.Create(CultureInfo.InvariantCulture, $"The logo is too large; it can be at most {CompanySettingsService.MaxLogoBytes / 1024} KB."));
        }

        using var buffer = new MemoryStream((int)upload.Length);
        await upload.CopyToAsync(buffer, cancellationToken);
        return await settings.SaveLogoAsync(buffer.ToArray(), cancellationToken);
    }

    [HttpDelete("company/logo")]
    [RequirePermission(Permissions.SettingsEdit)]
    [ProducesResponseType<BrandingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BrandingDto> DeleteLogo(CancellationToken cancellationToken) => settings.DeleteLogoAsync(cancellationToken);

    private const long MaxLogoUploadBytes = CompanySettingsService.MaxLogoBytes + 64 * 1024;
}

public sealed record CompanyLogoForm
{
    public IFormFile? Logo { get; init; }
}
