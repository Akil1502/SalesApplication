using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace SalesApp.Core.Validation;

/// <summary>Optional GSTIN: empty is allowed (unregistered customer); anything else must pass <see cref="Gstin.Validate"/>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class GstinAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return ValidationResult.Success;
        }

        var error = Gstin.Validate(text);
        return error is null
            ? ValidationResult.Success
            : new ValidationResult(error, validationContext.MemberName is { } member ? [member] : null);
    }
}

/// <summary>Indian PIN code: six digits, first digit 1-9.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class PincodeAttribute() : RegularExpressionAttribute("^[1-9][0-9]{5}$")
{
    public override string FormatErrorMessage(string name) => "PIN code must be 6 digits and can't start with 0.";
}

/// <summary>
/// Indian phone number, loosely: optional +91 or 0, then 10 digits; spaces and hyphens allowed.
/// Landlines with STD codes also fit in 10-11 digits, so we don't insist on a mobile prefix.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class IndianPhoneAttribute : ValidationAttribute
{
    [GeneratedRegex(@"^(\+91|0)?[0-9]{10}$")]
    private static partial Regex PhoneRegex();

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return ValidationResult.Success;
        }

        var compact = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return PhoneRegex().IsMatch(compact)
            ? ValidationResult.Success
            : new ValidationResult("Enter a 10-digit phone number, optionally starting with +91.",
                validationContext.MemberName is { } member ? [member] : null);
    }
}

/// <summary>
/// Limits a decimal to the scale of its database column (2 for money, 3 for quantities). Without this,
/// SQL Server would silently round 12.345 into a money column, and the user would never know.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DecimalPlacesAttribute(int places) : ValidationAttribute
{
    public int Places { get; } = places;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not decimal number || decimal.Round(number, Places) == number)
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            Places == 0 ? "Use a whole number." : $"Use at most {Places} decimal places.",
            validationContext.MemberName is { } member ? [member] : null);
    }
}

/// <summary>Indian Financial System Code: 4 letters, a zero, then 6 letters or digits.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class IfscAttribute() : RegularExpressionAttribute("^[A-Z]{4}0[A-Z0-9]{6}$")
{
    public override string FormatErrorMessage(string name) => "IFSC must be 11 characters, like SBIN0001234.";
}
