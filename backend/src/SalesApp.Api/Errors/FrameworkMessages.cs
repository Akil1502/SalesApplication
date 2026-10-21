namespace SalesApp.Api.Errors;

/// <summary>
/// Messages ASP.NET Core and Identity write themselves, as templates ({0} is the field or value), so they're
/// translated like the app's own (the translation tests collect every <see cref="L.Msg"/>). The forms check most of
/// this first, so people rarely see them.
/// </summary>
internal static class FrameworkMessages
{
    internal static readonly string[] DataAnnotations =
    [
        L.Msg("The {0} field is required."),
        L.Msg("The field {0} must be a string or array type with a maximum length of '{1}'."),
        L.Msg("The field {0} must be a string or array type with a minimum length of '{1}'."),
        L.Msg("The field {0} must be between {1} and {2}."),
        L.Msg("The field {0} must be a string with a maximum length of {1}."),
        L.Msg("The field {0} must be a string with a minimum length of {2} and a maximum length of {1}."),
        L.Msg("The {0} field is not a valid e-mail address."),
        L.Msg("The field {0} must match the regular expression '{1}'."),
    ];

    internal static readonly string[] ModelBinding =
    [
        L.Msg("A non-empty request body is required."),
        L.Msg("The value '{0}' is not valid for {1}."),
        L.Msg("The value '{0}' is invalid."),
        L.Msg("The field {0} must be a number."),
        L.Msg("A value is required."),
    ];

    internal static readonly string[] Identity =
    [
        L.Msg("Passwords must be at least {0} characters."),
        L.Msg("Passwords must have at least one non alphanumeric character."),
        L.Msg("Passwords must have at least one digit ('0'-'9')."),
        L.Msg("Passwords must have at least one lowercase ('a'-'z')."),
        L.Msg("Passwords must have at least one uppercase ('A'-'Z')."),
        L.Msg("Passwords must use at least {0} different characters."),
        L.Msg("Email '{0}' is already taken."),
        L.Msg("Username '{0}' is already taken."),
        L.Msg("Role name '{0}' is already taken."),
        L.Msg("Incorrect password."),
    ];
}
