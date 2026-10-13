using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
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
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<CompanySettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<CompanySettingsDto> UpdateCompany(CompanySettingsSaveRequest request, CancellationToken cancellationToken) =>
        settings.UpdateAsync(request, cancellationToken);
}
