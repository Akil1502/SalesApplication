using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SalesApp.Api.OpenApi;

/// <summary>
/// Timestamps are written by UtcDateTimeConverter, and a custom converter hides their shape from the OpenAPI
/// document (it would say "anything"). They are ISO 8601 strings in UTC, so say so: the TypeScript client then types
/// them as strings.
/// </summary>
internal sealed class DateTimeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DateTime) || type == typeof(DateTime?))
        {
            schema.Type = type == typeof(DateTime) ? JsonSchemaType.String : JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = "date-time";
        }

        return Task.CompletedTask;
    }
}
