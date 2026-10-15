using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Masters;

/// <summary>One product's rate for a period. EffectiveTo is null when it runs until a later period replaces it.</summary>
public sealed record ProductRateDto(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    ProductUnit Unit,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Note,
    string? UpdatedBy,
    DateTime UpdatedAtUtc);

public sealed record RateListRequest : PageRequest
{
    public int? ProductId { get; init; }

    /// <summary>Only periods that cover this day.</summary>
    public DateOnly? ActiveOn { get; init; }
}

public sealed record RateLineInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [DecimalRange(0, 99999999, ErrorMessage = "Rate must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal Rate { get; init; }
}

/// <summary>New rates for many products over one period, such as a month's rate list.</summary>
public sealed record RateBatchRequest
{
    public DateOnly EffectiveFrom { get; init; }

    /// <summary>The last day the rates apply; leave out to run until a later period replaces them.</summary>
    public DateOnly? EffectiveTo { get; init; }

    [MaxLength(200)]
    public string? Note { get; init; }

    [Required, MinLength(1, ErrorMessage = "Enter at least one rate."), MaxLength(5000)]
    public List<RateLineInput> Lines { get; init; } = [];
}

/// <param name="Created">Products that got a new period.</param>
/// <param name="Updated">Products that already had exactly this period, whose rate was changed.</param>
public sealed record RateBatchResult(int Created, int Updated);

public sealed record RateUpdateRequest
{
    [DecimalRange(0, 99999999, ErrorMessage = "Rate must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal Rate { get; init; }

    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    [MaxLength(200)]
    public string? Note { get; init; }
}

/// <summary>A row read from an uploaded rate sheet, ready to review before saving.</summary>
public sealed record RateImportLineDto(int ProductId, string Sku, string ProductName, decimal Rate);

/// <param name="Lines">Rows with a known SKU and a valid new rate.</param>
/// <param name="Problems">Rows that were skipped, and why, for example an unknown SKU.</param>
public sealed record RateImportResult(IReadOnlyList<RateImportLineDto> Lines, IReadOnlyList<string> Problems);
