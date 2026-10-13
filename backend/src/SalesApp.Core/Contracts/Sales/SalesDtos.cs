using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Sales;

// ---- Orders & quotations -------------------------------------------------------

public sealed record OrderListRequest : PageRequest
{
    public SalesOrderStatus? Status { get; init; }
    public int? CustomerId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record OrderListItemDto(
    int Id,
    string Number,
    SalesOrderStatus Status,
    OrderSource Source,
    DateOnly OrderDate,
    int CustomerId,
    string CustomerName,
    int LineCount,
    decimal GrandTotal,
    string? QuotationNumber,
    int? InvoiceId,
    string? InvoiceNumber,
    string CreatedByName);

public sealed record OrderLineDto(
    int Id,
    int LineNumber,
    int ProductId,
    string Sku,
    string ProductName,
    string HsnCode,
    ProductUnit Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal GstRate,
    decimal TaxableValue,
    decimal TaxAmount,
    decimal LineTotal,
    decimal QuantityOnHand);

/// <summary>An order or quotation with GST worked out at the rates on its lines.</summary>
public sealed record OrderDto(
    int Id,
    string Number,
    SalesOrderStatus Status,
    OrderSource Source,
    DateOnly OrderDate,
    DateOnly? ValidUntil,
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    string? CustomerGstin,
    string PlaceOfSupplyStateCode,
    string PlaceOfSupplyStateName,
    bool IsInterState,
    bool UsesUtgst,
    string? CustomerReference,
    string? Notes,
    string? QuotationNumber,
    DateTime? QuotedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    int? InvoiceId,
    string? InvoiceNumber,
    IReadOnlyList<OrderLineDto> Lines,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    string AmountInWords,
    string CreatedByName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion);

public sealed record OrderLineInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    public int ProductId { get; init; }

    [Range(typeof(decimal), "0.001", "99999999", ErrorMessage = "Quantity must be more than zero."), DecimalPlaces(3)]
    public decimal Quantity { get; init; }

    /// <summary>Price per unit before GST. Defaults to the product's list price when omitted.</summary>
    [Range(typeof(decimal), "0", "99999999", ErrorMessage = "Price must be between 0 and 9,99,99,999."), DecimalPlaces(2)]
    public decimal? UnitPrice { get; init; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Discount must be between 0% and 100%."), DecimalPlaces(2)]
    public decimal DiscountPercent { get; init; }
}

public sealed record OrderSaveRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a customer.")]
    public int CustomerId { get; init; }

    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? OrderDate { get; init; }

    public DateOnly? ValidUntil { get; init; }

    [MaxLength(50)]
    public string? CustomerReference { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    /// <summary>Only used when creating. Smart order entry (P5) sets the AI sources.</summary>
    public OrderSource Source { get; init; } = OrderSource.Manual;

    [Required, MinLength(1, ErrorMessage = "Add at least one product."), MaxLength(200)]
    public List<OrderLineInput> Lines { get; init; } = [];

    /// <summary>Required when updating.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Status changes carry the row version the user was looking at, so nobody acts on a stale screen.</summary>
public record OrderActionRequest
{
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record OrderConfirmRequest : OrderActionRequest
{
    /// <summary>Managers and admins may confirm an order that takes the customer over their credit limit.</summary>
    public bool OverrideCreditLimit { get; init; }
}

public sealed record OrderCancelRequest : OrderActionRequest
{
    [Required(ErrorMessage = "Give a reason for cancelling."), MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

// ---- Invoices ---------------------------------------------------------------------

public sealed record InvoiceListRequest : PageRequest
{
    public InvoiceStatus? Status { get; init; }
    public int? CustomerId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Only invoices with money still due after their due date.</summary>
    public bool OverdueOnly { get; init; }
}

public sealed record InvoiceListItemDto(
    int Id,
    string Number,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    int CustomerId,
    string CustomerName,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Balance,
    InvoiceStatus Status,
    int? DaysOverdue);

public sealed record InvoiceAddressDto(string Line1, string? Line2, string City, string StateCode, string StateName, string Pincode);

public sealed record InvoiceLineDto(
    int LineNumber,
    int ProductId,
    string Sku,
    string ProductName,
    string HsnCode,
    ProductUnit Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal TaxableValue,
    decimal GstRate,
    decimal CgstRate,
    decimal CgstAmount,
    decimal SgstRate,
    decimal SgstAmount,
    decimal IgstRate,
    decimal IgstAmount,
    decimal LineTotal);

/// <summary>Tax per HSN code and rate, printed under the line items (required on B2B invoices).</summary>
public sealed record HsnSummaryDto(string HsnCode, decimal GstRate, decimal TaxableValue, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount);

public sealed record PaymentDto(
    int Id,
    int InvoiceId,
    string InvoiceNumber,
    int CustomerId,
    string CustomerName,
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMode Mode,
    string? Reference,
    string? Notes,
    string CreatedByName,
    DateTime CreatedAtUtc);

/// <summary>A tax invoice as printed. Bank details and terms are the company's current ones.</summary>
public sealed record InvoiceDto(
    int Id,
    string Number,
    string FinancialYear,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    int SalesOrderId,
    string SalesOrderNumber,
    string SellerName,
    string SellerGstin,
    string SellerStateCode,
    string SellerAddress,
    int CustomerId,
    string CustomerName,
    string? CustomerGstin,
    InvoiceAddressDto BillingAddress,
    InvoiceAddressDto? ShippingAddress,
    string PlaceOfSupplyStateCode,
    string PlaceOfSupplyStateName,
    bool IsInterState,
    bool UsesUtgst,
    string? CustomerReference,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<HsnSummaryDto> HsnSummary,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal RoundOff,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Balance,
    int? DaysOverdue,
    string AmountInWords,
    IReadOnlyList<PaymentDto> Payments,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    string? BankName,
    string? BankAccountNumber,
    string? BankIfsc,
    string? BankBranch,
    string? Terms,
    string CreatedByName,
    DateTime CreatedAtUtc);

public sealed record InvoiceCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an order.")]
    public int SalesOrderId { get; init; }

    /// <summary>Defaults to today (India time). Can't be in the future or earlier than the last invoice this year.</summary>
    public DateOnly? InvoiceDate { get; init; }
}

public sealed record InvoiceCancelRequest
{
    [Required(ErrorMessage = "Give a reason for cancelling."), MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record InvoiceSummaryDto(
    decimal OutstandingAmount,
    int OutstandingCount,
    decimal OverdueAmount,
    int OverdueCount,
    decimal InvoicedThisMonth,
    decimal CollectedThisMonth);

// ---- Payments ---------------------------------------------------------------------

public sealed record PaymentListRequest : PageRequest
{
    public PaymentMode? Mode { get; init; }
    public int? CustomerId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record PaymentCreateRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an invoice.")]
    public int InvoiceId { get; init; }

    /// <summary>Defaults to today (India time).</summary>
    public DateOnly? PaymentDate { get; init; }

    [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Amount must be more than zero."), DecimalPlaces(2)]
    public decimal Amount { get; init; }

    public PaymentMode Mode { get; init; }

    [MaxLength(50)]
    public string? Reference { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }
}
