namespace SalesApp.Core.Common;

// Business-level exceptions thrown by services. They carry no HTTP knowledge;
// the API's exception handler maps each one to a status code and an ApiError body.

/// <summary>The requested record does not exist (or the caller may not see it).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>
/// Input is well-formed but breaks a business rule. <see cref="Errors"/> is keyed by
/// camelCase field name so the React forms can show the message next to the right input.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]>(errors);
    }

    public ValidationException(string field, string error)
        : this(error, new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>The record changed since the caller loaded it, or the action clashes with its current state.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The caller is authenticated but not allowed to do this.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
