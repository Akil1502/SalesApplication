using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SalesApp.AI;
using SalesApp.AI.Claude;
using SalesApp.AI.OrderEntry;
using SalesApp.Api.Errors;
using SalesApp.Api.Security;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Sales;

namespace SalesApp.Api.Controllers;

/// <summary>Whether AI features can be used, so the UI can show or hide them.</summary>
[Route("api/ai")]
public sealed class AiController(IClaudeGateway claude, IOptionsMonitor<AiOptions> options) : ApiControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<AiStatusDto>(StatusCodes.Status200OK)]
    public AiStatusDto Status()
    {
        if (claude.IsAvailable)
        {
            return new AiStatusDto(true, null, OrderEntry: true);
        }

        var reason = options.CurrentValue.Enabled ? "No Claude API key is configured." : "AI features are switched off.";
        return new AiStatusDto(false, reason, OrderEntry: false);
    }
}

/// <summary>The form smart order entry posts: pasted text, an uploaded file, or both.</summary>
public sealed class OrderExtractionForm
{
    public string? Text { get; init; }
    public IFormFile? File { get; init; }

    /// <summary>The customer the order is from. Required: the order's own details are only used to cross-check it.</summary>
    // Fully qualified: Core.Common also has a ValidationException, and this file uses both namespaces.
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Choose the customer the order is from.")]
    public int? CustomerId { get; init; }
}

/// <summary>
/// Smart order entry: read an order from a message, PDF or photo, then save it once the user has reviewed it.
/// Reading saves nothing; only the confirm call creates the order.
/// </summary>
[Route("api/orders/smart-entry")]
public sealed class SmartOrdersController(IOrderExtractionService extraction, ISmartOrderService smartOrders) : ApiControllerBase
{
    private const int MaxUploadBytes = 12 * 1024 * 1024;

    /// <summary>Reads a draft order with Claude. Nothing is saved.</summary>
    [HttpPost("extract")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(AiRateLimit.PolicyName)]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<OrderExtractionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiError>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<OrderExtractionDto> Extract([FromForm] OrderExtractionForm form, CancellationToken cancellationToken)
    {
        byte[]? file = null;
        if (form.File is { Length: > 0 })
        {
            if (form.File.Length > OrderFile.MaxPdfBytes)
            {
                throw new ValidationException("file", "Files can be at most 10 MB.");
            }

            using var buffer = new MemoryStream((int)form.File.Length);
            await form.File.CopyToAsync(buffer, cancellationToken);
            file = buffer.ToArray();
        }

        return await extraction.ExtractAsync(new OrderExtractionInput(form.Text, file, form.CustomerId), cancellationToken);
    }

    /// <summary>Saves the reviewed order as a draft and learns product aliases from the user's corrections.</summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderDto>> Confirm(SmartOrderConfirmRequest request, CancellationToken cancellationToken)
    {
        var order = await smartOrders.ConfirmAsync(request, cancellationToken);
        return Created($"/api/orders/{order.Id}", order);
    }
}
