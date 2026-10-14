using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SalesApp.AI;
using SalesApp.AI.Assistant;
using SalesApp.AI.Claude;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;

namespace SalesApp.Api.Controllers;

/// <summary>
/// The assistant chat. Conversations belong to the user who started them; nobody else (administrators included)
/// can read them. Replies stream as server-sent events.
/// </summary>
[Route("api/assistant")]
public sealed class AssistantController(
    IAssistantService assistant,
    IClaudeGateway claude,
    IOptions<JsonOptions> json,
    ILogger<AssistantController> logger) : ApiControllerBase
{
    /// <summary>The current user's most recent conversations, newest first.</summary>
    [HttpGet("conversations")]
    [ProducesResponseType<IReadOnlyList<AssistantConversationSummaryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AssistantConversationSummaryDto>> List(CancellationToken cancellationToken) =>
        assistant.ListAsync(cancellationToken);

    /// <summary>A conversation as it was shown: messages, lookups and drafts.</summary>
    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType<AssistantConversationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<AssistantConversationDto> Get(Guid id, CancellationToken cancellationToken) =>
        assistant.GetAsync(id, cancellationToken);

    [HttpDelete("conversations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await assistant.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Sends a message and streams the reply as <c>text/event-stream</c>: <c>conversation</c> (when a new one starts),
    /// <c>text</c> deltas, <c>tool</c> updates for each lookup, <c>draft</c> for each drafted message, then
    /// <c>done</c> with the cost, or <c>error</c>. Closing the connection stops the reply.
    /// </summary>
    [HttpPost("messages")]
    [EnableRateLimiting(AiRateLimit.PolicyName)]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task Send(AssistantSendRequest request, CancellationToken cancellationToken)
    {
        // Checked before the stream starts, so the browser gets an ordinary error response.
        if (!claude.IsAvailable)
        {
            throw new AiUnavailableException("The assistant needs the AI features, which are switched off or have no API key.");
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var events = new SseEvents(Response, json.Value.JsonSerializerOptions, cancellationToken);
        try
        {
            await assistant.SendAsync(request, events, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user pressed Stop or closed the page; what had streamed is saved and nobody is listening.
        }
        catch (Exception exception) when (!Response.HasStarted && exception is NotFoundException or ConflictException or ValidationException or AiUnavailableException)
        {
            // Nothing streamed yet (an unknown or full conversation): let the error handler send a normal response.
            Response.ContentType = null;
            throw;
        }
        catch (Exception exception) when (exception is AiUnavailableException or AiBudgetExceededException or AiRefusedException
            or AiServiceException or ValidationException or ConflictException or NotFoundException)
        {
            await events.SendAsync("error", new { message = exception is AiRefusedException ? "Claude declined to answer that message." : exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Assistant reply failed");
            await events.SendAsync("error", new { message = "Something went wrong while replying. Please try again." });
        }
    }

    private sealed class SseEvents(HttpResponse response, JsonSerializerOptions options, CancellationToken cancellationToken) : IAssistantEvents
    {
        public Task StartedAsync(AssistantStartedEvent started) => SendAsync("conversation", started);
        public Task TextAsync(string delta) => SendAsync("text", new AssistantTextEvent(delta));
        public Task ToolAsync(AssistantToolDto tool) => SendAsync("tool", tool);
        public Task DraftAsync(AssistantDraftDto draft) => SendAsync("draft", draft);
        public Task DoneAsync(AssistantDoneEvent done) => SendAsync("done", done);

        public async Task SendAsync(string name, object data)
        {
            await response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, options)}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
}
