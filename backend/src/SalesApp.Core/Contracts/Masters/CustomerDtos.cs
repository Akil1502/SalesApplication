using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Masters;

public sealed record CustomerListRequest : PageRequest
{
    public Guid? AssignedRepId { get; init; }

    [StringLength(2, MinimumLength = 2)]
    public string? StateCode { get; init; }
}

public sealed record CustomerListItemDto(
    int Id,
    string Code,
    string Name,
    string? Gstin,
    string City,
    string StateName,
    string? Phone,
    string? AssignedRepName,
    bool IsActive);

public sealed record AddressDto(string Line1, string? Line2, string City, string StateCode, string StateName, string Pincode);

/// <summary>A customer with full details. ShippingAddress is null when goods ship to the billing address.</summary>
public sealed record CustomerDto(
    int Id,
    string Code,
    string Name,
    string? TradeName,
    string? Gstin,
    string? Pan,
    string? ContactPerson,
    string? Phone,
    string? Email,
    AddressDto BillingAddress,
    AddressDto? ShippingAddress,
    decimal? CreditLimit,
    int PaymentTermsDays,
    Guid? AssignedRepId,
    string? AssignedRepName,
    string? Notes,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion);

public sealed record AddressInput
{
    [Required, MaxLength(120)]
    public string Line1 { get; init; } = string.Empty;

    [MaxLength(120)]
    public string? Line2 { get; init; }

    [Required, MaxLength(60)]
    public string City { get; init; } = string.Empty;

    [Required, StringLength(2, MinimumLength = 2, ErrorMessage = "Choose a state.")]
    public string StateCode { get; init; } = string.Empty;

    [Required, Pincode]
    public string Pincode { get; init; } = string.Empty;
}

public sealed record CustomerSaveRequest
{
    /// <summary>Optional on create (generated when blank); ignored on update because codes are permanent.</summary>
    [MaxLength(20), RegularExpression("^[A-Za-z0-9-]*$", ErrorMessage = "Code may contain letters, digits and '-'.")]
    public string? Code { get; init; }

    [Required, MaxLength(150)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(150)]
    public string? TradeName { get; init; }

    [Gstin]
    public string? Gstin { get; init; }

    [MaxLength(100)]
    public string? ContactPerson { get; init; }

    [IndianPhone, MaxLength(20)]
    public string? Phone { get; init; }

    [EmailAddress, MaxLength(256)]
    public string? Email { get; init; }

    [Required]
    public AddressInput BillingAddress { get; init; } = new();

    public bool ShippingSameAsBilling { get; init; } = true;

    /// <summary>Used only when <see cref="ShippingSameAsBilling"/> is false.</summary>
    public AddressInput? ShippingAddress { get; init; }

    /// <summary>Managers and admins only; ignored for sales reps.</summary>
    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Credit limit must be between 0 and 99,99,99,999."), DecimalPlaces(2)]
    public decimal? CreditLimit { get; init; }

    /// <summary>Managers and admins only; ignored for sales reps.</summary>
    [Range(0, 365, ErrorMessage = "Payment terms must be between 0 and 365 days.")]
    public int PaymentTermsDays { get; init; } = 30;

    /// <summary>Managers and admins only; a sales rep's new customers are assigned to themselves.</summary>
    public Guid? AssignedRepId { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Required when updating; the value from the last GET, for optimistic concurrency.</summary>
    public string? RowVersion { get; init; }
}
