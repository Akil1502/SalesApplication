using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SalesApp.Api.Errors;

/// <summary>
/// Replaces ASP.NET Core's default validation ProblemDetails with our <see cref="ApiError"/>,
/// so the React app handles exactly one error shape.
/// </summary>
internal static class ValidationErrorResponse
{
    public static IActionResult Create(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => ToFieldName(entry.Key))
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(entry => entry.Value!.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "This value is not valid." : e.ErrorMessage)
                    .Distinct()
                    .ToArray());

        var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(new ApiError("Some fields need attention.", traceId, errors));
    }

    /// <summary>
    /// Model-state keys arrive as "Email", "Lines[0].Quantity" or "$.email" (JSON binding errors).
    /// Normalise them to the camelCase paths the client uses: "email", "lines[0].quantity".
    /// </summary>
    internal static string ToFieldName(string key)
    {
        var trimmed = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key.TrimStart('$');
        if (trimmed.Length == 0)
        {
            return "body";
        }

        return string.Join('.', trimmed.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}
