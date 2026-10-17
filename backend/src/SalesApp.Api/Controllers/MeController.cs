using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SalesApp.Api.Errors;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Api.Controllers;

/// <summary>The signed-in person's own settings. Anyone signed in may read and change their own, and only their own.</summary>
[Route("api/me")]
public sealed class MeController(IUserSettingsService settings) : ApiControllerBase
{
    /// <summary>All of my settings, by name. The values are JSON the web app owns.</summary>
    [HttpGet("settings")]
    [ProducesResponseType<IReadOnlyDictionary<string, JsonElement>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyDictionary<string, JsonElement>> Settings(CancellationToken cancellationToken) =>
        settings.ListAsync(CurrentUserId, cancellationToken);

    /// <summary>Saves one setting (at most 32 KB of JSON), replacing any earlier value.</summary>
    [HttpPut("settings/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(string key, [FromBody] JsonElement value, CancellationToken cancellationToken)
    {
        await settings.SaveAsync(CurrentUserId, key, value, cancellationToken);
        return NoContent();
    }

    [HttpDelete("settings/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string key, CancellationToken cancellationToken)
    {
        await settings.DeleteAsync(CurrentUserId, key, cancellationToken);
        return NoContent();
    }
}
