using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>An uploaded rate sheet.</summary>
public sealed class RateImportForm
{
    public IFormFile? File { get; init; }
}

/// <summary>Rate lists: products' rates for periods. Administrators by default (Rate updates permission).</summary>
[RequirePermission(Permissions.RatesView)]
public sealed class RatesController(IRateService rates, IBusinessClock clock, IAuditLog audit) : ApiControllerBase
{
    private const long MaxSheetBytes = 5 * 1024 * 1024;

    [HttpGet]
    [ProducesResponseType<PagedResult<ProductRateDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ProductRateDto>> List([FromQuery] RateListRequest request, CancellationToken cancellationToken) =>
        rates.ListAsync(request, cancellationToken);

    /// <summary>Saves rates for many products over one period, such as a month's rate list.</summary>
    [HttpPost("batch")]
    [RequirePermission(Permissions.RatesEdit)]
    [ProducesResponseType<RateBatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public Task<RateBatchResult> SaveBatch(RateBatchRequest request, CancellationToken cancellationToken) =>
        rates.SaveBatchAsync(request, cancellationToken);

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.RatesEdit)]
    [ProducesResponseType<ProductRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProductRateDto> Update(int id, RateUpdateRequest request, CancellationToken cancellationToken) =>
        rates.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.RatesEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await rates.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>A sheet of active products with their price on a day, to fill in and upload.</summary>
    [HttpGet("excel")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Excel([FromQuery] DateOnly? on, [FromQuery] int? categoryId, [FromQuery] string? q, CancellationToken cancellationToken)
    {
        var day = on ?? clock.Today;
        var bytes = await rates.ExportAsync(day, categoryId, q, cancellationToken);
        await audit.RecordAsync(new AuditEvent(AuditAction.Exported,
            Summary: $"Downloaded the rate sheet with prices on {day.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."), cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"rates-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx");
    }

    /// <summary>Reads a filled-in rate sheet and returns its rows to review. Nothing is saved.</summary>
    [HttpPost("import")]
    [RequirePermission(Permissions.RatesEdit)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxSheetBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSheetBytes + 64 * 1024)]
    [ProducesResponseType<RateImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<RateImportResult> Import([FromForm] RateImportForm form, CancellationToken cancellationToken)
    {
        if (form.File is not { Length: > 0 } file)
        {
            throw new ValidationException("file", "Choose the filled-in rate sheet (.xlsx).");
        }

        if (file.Length > MaxSheetBytes)
        {
            throw new ValidationException("file", "The sheet can be at most 5 MB.");
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        // An .xlsx file is a zip archive, which starts with "PK".
        if (buffer.Length < 4 || buffer.ReadByte() != 'P' || buffer.ReadByte() != 'K')
        {
            throw new ValidationException("file", "That isn't an Excel workbook (.xlsx). Download the rate sheet, fill it in and upload it.");
        }

        buffer.Position = 0;
        return await rates.ImportAsync(buffer, cancellationToken);
    }
}
