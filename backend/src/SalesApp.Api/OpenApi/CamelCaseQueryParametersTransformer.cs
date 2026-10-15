using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SalesApp.Api.OpenApi;

/// <summary>
/// [FromQuery] request objects are documented with their C# property names (Q, PageSize). Model binding
/// is case-insensitive, so this renames them to camelCase (q, pageSize) to match the JSON bodies and
/// give the generated TypeScript client consistent names.
/// </summary>
internal sealed class CamelCaseQueryParametersTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var parameter in operation.Parameters ?? [])
        {
            if (parameter is OpenApiParameter { In: ParameterLocation.Query, Name: { } name } concrete)
            {
                concrete.Name = JsonNamingPolicy.CamelCase.ConvertName(name);
            }
        }

        return Task.CompletedTask;
    }
}
