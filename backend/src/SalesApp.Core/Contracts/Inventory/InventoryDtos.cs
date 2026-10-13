using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Inventory;

public enum StockStatus
{
    InStock,
    Low,
    OutOfStock,
}

public sealed record StockLevelListRequest : PageRequest
{
    public int? CategoryId { get; init; }

    /// <summary>Low includes out-of-stock items (anything at or below its reorder level).</summary>
    public StockStatus? Status { get; init; }
}

/// <summary>Stock position of one product. StockValue (quantity × cost) is null for sales reps.</summary>
public sealed record StockLevelDto(
    int ProductId,
    string Sku,
    string Name,
    string CategoryName,
    ProductUnit Unit,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal ReorderQuantity,
    StockStatus Status,
    DateTime? LastMovementAtUtc,
    decimal? StockValue);

/// <summary>Headline numbers for the stock page. TotalStockValue is null for sales reps.</summary>
public sealed record StockSummaryDto(int ActiveProducts, int LowStock, int OutOfStock, decimal? TotalStockValue);

public sealed record StockMovementListRequest : PageRequest
{
    public int? ProductId { get; init; }
    public StockMovementType? Type { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record StockMovementDto(
    long Id,
    int ProductId,
    string Sku,
    string ProductName,
    ProductUnit Unit,
    StockMovementType Type,
    DateOnly MovementDate,
    decimal Quantity,
    decimal BalanceAfter,
    decimal? UnitCost,
    AdjustmentReason? Reason,
    string? ReferenceType,
    int? ReferenceId,
    string? ReferenceNumber,
    string? Notes,
    string CreatedByName,
    DateTime CreatedAtUtc);

public sealed record StockReceiptLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    public int ProductId { get; init; }

    [Range(typeof(decimal), "0.001", "99999999", ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    [Range(typeof(decimal), "0", "99999999", ErrorMessage = "Cost must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal? UnitCost { get; init; }
}

public sealed record StockReceiptCreateRequest
{
    /// <summary>Defaults to today (India time). Can't be in the future.</summary>
    public DateOnly? ReceiptDate { get; init; }

    [MaxLength(150)]
    public string? SupplierName { get; init; }

    [MaxLength(50)]
    public string? SupplierReference { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }

    [Required, MinLength(1, ErrorMessage = "Add at least one product."), MaxLength(200)]
    public List<StockReceiptLineInput> Lines { get; init; } = [];
}

public sealed record StockReceiptListItemDto(
    int Id,
    string Number,
    DateOnly ReceiptDate,
    string? SupplierName,
    string? SupplierReference,
    int LineCount,
    decimal? TotalCost,
    string CreatedByName,
    DateTime CreatedAtUtc);

public sealed record StockReceiptLineDto(
    long MovementId,
    int ProductId,
    string Sku,
    string ProductName,
    ProductUnit Unit,
    decimal Quantity,
    decimal? UnitCost);

public sealed record StockReceiptDto(
    int Id,
    string Number,
    DateOnly ReceiptDate,
    string? SupplierName,
    string? SupplierReference,
    string? Notes,
    string CreatedByName,
    DateTime CreatedAtUtc,
    IReadOnlyList<StockReceiptLineDto> Lines);

public enum AdjustmentMode
{
    /// <summary>Add or remove the given quantity (signed).</summary>
    Change,

    /// <summary>Set stock to the counted quantity; the difference is calculated at the moment of posting.</summary>
    SetCount,
}

public sealed record StockAdjustmentRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    public int ProductId { get; init; }

    public AdjustmentMode Mode { get; init; }

    /// <summary>For Change: the signed amount (non-zero). For SetCount: the counted quantity (zero or more).</summary>
    [Range(typeof(decimal), "-99999999", "99999999"), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    public AdjustmentReason Reason { get; init; }

    public DateOnly? MovementDate { get; init; }

    /// <summary>Why the stock changed. Required, because adjustments are what auditors ask about.</summary>
    [Required(ErrorMessage = "Explain the adjustment."), MaxLength(500)]
    public string Notes { get; init; } = string.Empty;
}
