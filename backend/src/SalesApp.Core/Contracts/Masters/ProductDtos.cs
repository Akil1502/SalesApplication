using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Masters;

public sealed record ProductListRequest : PageRequest
{
    public int? CategoryId { get; init; }

    /// <summary>Only products at or below their reorder level.</summary>
    public bool LowStockOnly { get; init; }

    /// <summary>The day to price products on (an order's date); today when left out.</summary>
    public DateOnly? PriceOn { get; init; }
}

/// <summary>
/// A product in a list. Price is its price on the requested day: the rate for that period (HasRate) or else the list
/// price. New order lines start at Price.
/// </summary>
public sealed record ProductListItemDto(
    int Id,
    string Sku,
    string Name,
    string CategoryName,
    string HsnCode,
    decimal GstRate,
    ProductUnit Unit,
    decimal ListPrice,
    decimal Price,
    bool HasRate,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    bool IsActive);

/// <summary>A product with full details. CostPrice is null for sales reps: cost and margin are manager-only.</summary>
public sealed record ProductDto(
    int Id,
    string Sku,
    string Name,
    string? Description,
    int CategoryId,
    string CategoryName,
    int HsnCodeId,
    string HsnCode,
    decimal GstRate,
    ProductUnit Unit,
    decimal ListPrice,
    decimal? CostPrice,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    int LeadTimeDays,
    decimal QuantityOnHand,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion);

public sealed record ProductSaveRequest
{
    [Required, MaxLength(40), RegularExpression("^[A-Za-z0-9][A-Za-z0-9._/-]*$",
        ErrorMessage = "SKU may contain letters, digits, '.', '_', '/' and '-', starting with a letter or digit.")]
    public string Sku { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a category.")]
    public int CategoryId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose an HSN code.")]
    public int HsnCodeId { get; init; }

    [EnumDataType(typeof(ProductUnit))]
    public ProductUnit Unit { get; init; }

    [DecimalRange(0, 99999999, ErrorMessage = "Price must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal ListPrice { get; init; }

    [DecimalRange(0, 99999999, ErrorMessage = "Cost must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal? CostPrice { get; init; }

    [DecimalRange(0, 99999999), DecimalPlaces(3)]
    public decimal ReorderLevel { get; init; }

    [DecimalRange(0, 99999999), DecimalPlaces(3)]
    public decimal ReorderQuantity { get; init; }

    [Range(0, 365, ErrorMessage = "Lead time must be between 0 and 365 days.")]
    public int LeadTimeDays { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Required when updating; the value from the last GET, for optimistic concurrency.</summary>
    public string? RowVersion { get; init; }
}
