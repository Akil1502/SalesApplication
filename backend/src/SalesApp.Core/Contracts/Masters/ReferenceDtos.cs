using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Validation;

namespace SalesApp.Core.Contracts.Masters;

public sealed record StateDto(string Code, string Name, bool UsesUtgst);

public sealed record UserLookupDto(Guid Id, string FullName, string Email);

public sealed record CategoryDto(int Id, string Name, string? Description, bool IsActive, int ProductCount);

public sealed record CategorySaveRequest
{
    [Required, MaxLength(80)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record HsnCodeDto(int Id, string Code, string Description, decimal GstRate, bool IsActive, int ProductCount);

public sealed record HsnCodeSaveRequest
{
    [Required, RegularExpression("^([0-9]{4}|[0-9]{6}|[0-9]{8})$", ErrorMessage = "HSN code must be 4, 6 or 8 digits.")]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(300)]
    public string Description { get; init; } = string.Empty;

    /// <summary>Total GST %, 0 to 40 (the highest slab after the 2025 rationalisation).</summary>
    [DecimalRange(0, 40, ErrorMessage = "GST rate must be between 0% and 40%."), DecimalPlaces(2)]
    public decimal GstRate { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record CompanySettingsDto(
    string LegalName,
    string? TradeName,
    string Gstin,
    string StateCode,
    string StateName,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Pincode,
    string? Phone,
    string? Email,
    string? BankName,
    string? BankAccountNumber,
    string? BankIfsc,
    string? BankBranch,
    string InvoicePrefix,
    string QuotationPrefix,
    string? InvoiceTerms,
    string RowVersion);

public sealed record CompanySettingsSaveRequest
{
    [Required, MaxLength(150)]
    public string LegalName { get; init; } = string.Empty;

    [MaxLength(150)]
    public string? TradeName { get; init; }

    [Required, Gstin]
    public string Gstin { get; init; } = string.Empty;

    [Required, StringLength(2, MinimumLength = 2)]
    public string StateCode { get; init; } = string.Empty;

    [Required, MaxLength(120)]
    public string AddressLine1 { get; init; } = string.Empty;

    [MaxLength(120)]
    public string? AddressLine2 { get; init; }

    [Required, MaxLength(60)]
    public string City { get; init; } = string.Empty;

    [Required, Pincode]
    public string Pincode { get; init; } = string.Empty;

    [IndianPhone, MaxLength(20)]
    public string? Phone { get; init; }

    [EmailAddress, MaxLength(256)]
    public string? Email { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    [RegularExpression("^[0-9]{9,18}$", ErrorMessage = "Account number must be 9 to 18 digits.")]
    public string? BankAccountNumber { get; init; }

    [Ifsc]
    public string? BankIfsc { get; init; }

    [MaxLength(100)]
    public string? BankBranch { get; init; }

    /// <summary>At most 4 letters so "PREF/26-27/00001" stays within GST's 16-character invoice number limit.</summary>
    [Required, RegularExpression("^[A-Z]{1,4}$", ErrorMessage = "Use 1 to 4 capital letters.")]
    public string InvoicePrefix { get; init; } = "INV";

    [Required, RegularExpression("^[A-Z]{1,4}$", ErrorMessage = "Use 1 to 4 capital letters.")]
    public string QuotationPrefix { get; init; } = "QT";

    [MaxLength(2000)]
    public string? InvoiceTerms { get; init; }

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}
