using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Reporting;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>Standard reports, on screen or as Excel. Without dates they cover the financial year to date.</summary>
[RequirePermission(Permissions.ReportsView)]
public sealed class ReportsController(IReportService reports, ICompanySettingsService settings) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReportDefinitionDto>>(StatusCodes.Status200OK)]
    public IReadOnlyList<ReportDefinitionDto> List() => reports.List();

    [HttpGet("{key}")]
    [ProducesResponseType<ReportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ReportResult> Run(string key, [FromQuery] ReportRequest request, CancellationToken cancellationToken) =>
        reports.RunAsync(key, request, cancellationToken);

    /// <summary>The same report as an .xlsx file.</summary>
    [HttpGet("{key}/excel")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excel(string key, [FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        var report = await reports.RunAsync(key, request, cancellationToken);
        var company = await settings.GetAsync(cancellationToken);
        var bytes = ExcelReportWriter.Write(report, company.TradeName ?? company.LegalName);
        var suffix = report.From is { } from && report.To is { } to ? $"{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}" : DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{report.Key}_{suffix}.xlsx");
    }
}

/// <summary>Home dashboard figures.</summary>
[RequirePermission(Permissions.DashboardView)]
public sealed class DashboardController(IDashboardService dashboard) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    public Task<DashboardDto> Get(CancellationToken cancellationToken) => dashboard.GetAsync(cancellationToken);
}
