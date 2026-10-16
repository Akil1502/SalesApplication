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

/// <summary>A goods receipt in a list. Receipts are recorded against purchase orders; older ones may have none.</summary>
public sealed record StockReceiptListItemDto(
    int Id,
    string Number,
    DateOnly ReceiptDate,
    int? PurchaseOrderId,
    string? PurchaseOrderNumber,
    int? SupplierId,
    string? SupplierName,
    string? SupplierReference,
    int LineCount,
    decimal? TotalCost,
    string CreatedByName,
    DateTime CreatedAtUtc);

public sealed record StockReceiptLineDto(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    ProductUnit Unit,
    decimal Quantity,
    decimal? UnitCost,
    decimal QuantityRejected,
    string? RejectionReason);

public sealed record StockReceiptDto(
    int Id,
    string Number,
    DateOnly ReceiptDate,
    int? PurchaseOrderId,
    string? PurchaseOrderNumber,
    int? SupplierId,
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
    [DecimalRange(-99999999, 99999999), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    public AdjustmentReason Reason { get; init; }

    public DateOnly? MovementDate { get; init; }

    /// <summary>Why the stock changed. Required, because adjustments are what auditors ask about.</summary>
    [Required(ErrorMessage = "Explain the adjustment."), MaxLength(500)]
    public string Notes { get; init; } = string.Empty;
}
