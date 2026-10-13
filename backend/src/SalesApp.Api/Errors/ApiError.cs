using System.Text.Json.Serialization;

namespace SalesApp.Api.Errors;

/// <summary>
/// The one error shape every endpoint returns. <see cref="TraceId"/> matches the server log
/// entry, so a user can quote it when reporting a problem. <see cref="Errors"/> is only present
/// for validation failures and is keyed by camelCase field name.
/// </summary>
public sealed record ApiError(
    string Message,
    string TraceId,
    // Attribute (not just the MVC serializer setting) because auth middleware writes this
    // record with default JSON options, outside MVC.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string[]>? Errors = null);
