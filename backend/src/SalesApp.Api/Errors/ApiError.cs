using System.Text.Json.Serialization;

namespace SalesApp.Api.Errors;

/// <summary>
/// The one error shape every endpoint returns. <see cref="TraceId"/> matches the server log
/// entry, so a user can quote it when reporting a problem. <see cref="Errors"/> is only present
/// for validation failures and is keyed by camelCase field name. Messages are written in English and
/// translated here into the request's language (Tamil when the app is in Tamil).
/// </summary>
public sealed record ApiError(string Message, string TraceId, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public string Message { get; init; } = L.T(Message);

    // Attribute (not just the MVC serializer setting) because auth middleware writes this
    // record with default JSON options, outside MVC.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; } =
        Errors?.ToDictionary(field => field.Key, field => field.Value.Select(message => L.T(message)).ToArray());
}
