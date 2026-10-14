using System.Diagnostics;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.AI.Claude;

/// <summary>The one way the app talks to Claude. Features build a <see cref="ClaudeRequest"/>; the gateway does the rest.</summary>
public interface IClaudeGateway
{
    /// <summary>False when AI is switched off or no API key is configured; AI screens are hidden then.</summary>
    bool IsAvailable { get; }

    Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken cancellationToken);

    /// <summary>One turn of a tool-using conversation: Claude either calls tools or gives its final reply.</summary>
    Task<ClaudeTurn> SendTurnAsync(ClaudeConversation conversation, CancellationToken cancellationToken);

    /// <summary>
    /// The same turn, streamed: <paramref name="onEvent"/> sees the reply text as it's written and each tool call as
    /// it starts. Cancelling stops the stream, so Anthropic stops generating (and billing) the reply.
    /// </summary>
    Task<ClaudeTurn> StreamTurnAsync(ClaudeConversation conversation, Func<ClaudeStreamEvent, Task> onEvent, CancellationToken cancellationToken);
}

/// <summary>Holds the SDK client for the app's lifetime. It's only created when an API key is configured.</summary>
public sealed class ClaudeClientProvider(IOptions<AnthropicOptions> options)
{
    private readonly Lazy<AnthropicClient?> _client = new(() =>
        string.IsNullOrWhiteSpace(options.Value.ApiKey) ? null : new AnthropicClient { ApiKey = options.Value.ApiKey });

    public bool HasApiKey => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public AnthropicClient? Client => _client.Value;
}

/// <summary>
/// Sends requests with the feature's configured model, effort and token limit; enforces the per-user daily budget;
/// checks the stop reason before reading the reply; maps SDK errors to messages users can act on; and logs every
/// call (tokens, cache use, estimated cost, outcome) to <see cref="AiUsageLog"/>.
/// </summary>
public sealed class ClaudeGateway(
    ClaudeClientProvider clients,
    IOptionsMonitor<AiOptions> options,
    IAiUsageRecorder usage,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<ClaudeGateway> logger) : IClaudeGateway
{
    public bool IsAvailable => options.CurrentValue.Enabled && clients.HasApiKey;

    public async Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken cancellationToken)
    {
        var (message, usageSummary) = await CreateAsync(request.Feature, feature => BuildParameters(request, feature), cancellationToken);
        var text = string.Concat(message.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
        return new ClaudeResponse(text, usageSummary, message.ID);
    }

    public async Task<ClaudeTurn> StreamTurnAsync(ClaudeConversation conversation, Func<ClaudeStreamEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        var stream = new ClaudeStreamAccumulator();
        var (call, usageSummary) = await ExecuteAsync(
            conversation.Feature,
            feature => BuildConversationParameters(conversation, feature),
            async (client, parameters, token) =>
            {
                await foreach (var streamEvent in client.Messages.CreateStreaming(parameters, token).WithCancellation(token))
                {
                    if (stream.Add(streamEvent) is { } shown)
                    {
                        await onEvent(shown);
                    }
                }

                return Result(stream);
            },
            () => Result(stream),
            cancellationToken);

        return new ClaudeTurn(stream.Blocks(), call.StopReason ?? "", usageSummary, call.MessageId);

        static CallResult Result(ClaudeStreamAccumulator s) =>
            new(s.InputTokens, s.OutputTokens, s.CacheReadTokens, s.CacheWriteTokens, s.StopReason, s.MessageId);
    }

    public async Task<ClaudeTurn> SendTurnAsync(ClaudeConversation conversation, CancellationToken cancellationToken)
    {
        var (message, usageSummary) = await CreateAsync(conversation.Feature, feature => BuildConversationParameters(conversation, feature), cancellationToken);
        var blocks = new List<ClaudeBlock>();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text))
            {
                blocks.Add(new ClaudeTextBlock(text.Text));
            }
            else if (block.TryPickThinking(out var thinking))
            {
                blocks.Add(new ClaudeThinkingBlock(thinking.Thinking, thinking.Signature));
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                blocks.Add(new ClaudeRedactedThinkingBlock(redacted.Data));
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                blocks.Add(new ClaudeToolUseBlock(toolUse.ID, toolUse.Name, toolUse.Input));
            }
        }

        return new ClaudeTurn(blocks, message.StopReason?.Raw() ?? "", usageSummary, message.ID);
    }

    /// <summary>Token counts and stop reason of one call, whether it was streamed or not.</summary>
    private sealed record CallResult(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, string? StopReason, string? MessageId);

    private async Task<(Message Message, ClaudeUsage Usage)> CreateAsync(
        string featureName, Func<AiFeatureOptions, MessageCreateParams> build, CancellationToken cancellationToken)
    {
        Message? message = null;
        var (_, usageSummary) = await ExecuteAsync(
            featureName,
            build,
            async (client, parameters, token) =>
            {
                message = await client.Messages.Create(parameters, token);
                return new CallResult(message.Usage.InputTokens, message.Usage.OutputTokens, message.Usage.CacheReadInputTokens ?? 0,
                    message.Usage.CacheCreationInputTokens ?? 0, message.StopReason?.Raw(), message.ID);
            },
            partial: null,
            cancellationToken);
        return (message!, usageSummary);
    }

    /// <summary>
    /// One API call with everything around it: availability and budget checks, the feature's settings, error
    /// translation, the stop-reason check (refusals and cut-off replies are never treated as answers) and the usage log.
    /// <paramref name="partial"/> gives the tokens known so far when a streamed call is stopped part-way.
    /// </summary>
    private async Task<(CallResult Call, ClaudeUsage Usage)> ExecuteAsync(
        string featureName,
        Func<AiFeatureOptions, MessageCreateParams> build,
        Func<AnthropicClient, MessageCreateParams, CancellationToken, Task<CallResult>> call,
        Func<CallResult>? partial,
        CancellationToken cancellationToken)
    {
        var ai = options.CurrentValue;
        if (!ai.Enabled)
        {
            throw new AiUnavailableException("AI features are switched off.");
        }

        var client = clients.Client ?? throw new AiUnavailableException("AI features need a Claude API key. An administrator can add one (see the README).");

        if (await usage.SpentInLastDayAsync(currentUser.UserId, cancellationToken) >= ai.DailyBudgetUsdPerUser)
        {
            throw new AiBudgetExceededException("You've reached your AI usage limit for the last 24 hours. Try again later, or ask an administrator to raise it.");
        }

        var feature = ai.For(featureName);
        var log = new AiUsageLog
        {
            Feature = featureName,
            Model = feature.Model,
            UserId = currentUser.UserId,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        var started = Stopwatch.GetTimestamp();

        CallResult result;
        try
        {
            result = await call(client, build(feature), cancellationToken);
        }
        catch (Exception exception) when (exception is AnthropicException or OperationCanceledException or AiServiceException)
        {
            log.DurationMs = ElapsedMs(started);
            log.Outcome = AiOutcome.Failed;
            log.Error = Shorten(exception is OperationCanceledException ? "Cancelled" : $"{exception.GetType().Name}: {exception.Message}");
            // A stopped stream has still used the prompt tokens (and the output generated so far, which the API
            // only reports at the end, so the estimate is a little low).
            if (partial?.Invoke() is { } soFar)
            {
                Fill(log, soFar, ai.Pricing.GetValueOrDefault(feature.Model));
            }

            // Recorded even when the request was cancelled: the tokens were used either way.
            await usage.RecordAsync(log, CancellationToken.None);

            if (exception is OperationCanceledException or AiServiceException)
            {
                throw;
            }

            logger.LogWarning(exception, "Claude call for {Feature} failed", featureName);
            throw Translate(exception);
        }

        Fill(log, result, ai.Pricing.GetValueOrDefault(feature.Model));
        log.DurationMs = ElapsedMs(started);

        // The stop reason decides whether the content can be trusted: a refusal or a cut-off reply may not match
        // the requested schema or contain half a tool call, so it's never used as if it were a normal answer.
        var stopReason = result.StopReason;
        log.Outcome = stopReason switch
        {
            "refusal" => AiOutcome.Refused,
            "max_tokens" => AiOutcome.Truncated,
            _ => AiOutcome.Success,
        };
        if (log.Outcome != AiOutcome.Success)
        {
            log.Error = $"Stop reason: {stopReason}";
        }

        await usage.RecordAsync(log, cancellationToken);

        if (log.Outcome == AiOutcome.Refused)
        {
            throw new AiRefusedException("Claude declined to process this content.");
        }

        if (log.Outcome == AiOutcome.Truncated)
        {
            throw new AiServiceException("Claude's reply was cut off at the length limit. Try a shorter input.", StatusCodesBadGateway);
        }

        return (result, new ClaudeUsage(feature.Model, log.InputTokens, log.OutputTokens, log.CacheReadTokens, log.CacheWriteTokens, log.CostUsd, log.DurationMs));
    }

    private static void Fill(AiUsageLog log, CallResult result, ModelPricing? pricing)
    {
        log.InputTokens = (int)result.InputTokens;
        log.OutputTokens = (int)result.OutputTokens;
        log.CacheReadTokens = (int)result.CacheReadTokens;
        log.CacheWriteTokens = (int)result.CacheWriteTokens;
        log.CostUsd = AiCost.Estimate(pricing, log.InputTokens, log.OutputTokens, log.CacheReadTokens, log.CacheWriteTokens);
        log.RequestId = result.MessageId;
    }

    private const int StatusCodesBadGateway = 502;

    /// <summary>
    /// Maps the request onto the SDK. The cache breakpoint goes on the last system block marked
    /// <see cref="ClaudeSystemBlock.CacheAfter"/>, so instructions and reference data are cached together and the
    /// per-request content (the customer's message, the file) follows the cached prefix.
    /// </summary>
    internal static MessageCreateParams BuildParameters(ClaudeRequest request, AiFeatureOptions feature)
    {
        var system = request.System
            .Select(block => new TextBlockParam { Text = block.Text, CacheControl = block.CacheAfter ? new CacheControlEphemeral() : null })
            .ToList();

        var content = new List<ContentBlockParam>();
        foreach (var part in request.Content)
        {
            switch (part)
            {
                case ClaudePdf pdf:
                    content.Add(new DocumentBlockParam { Source = new Base64PdfSource { Data = Convert.ToBase64String(pdf.Data) } });
                    break;
                case ClaudeImage image:
                    content.Add(new ImageBlockParam { Source = new Base64ImageSource { Data = Convert.ToBase64String(image.Data), MediaType = ImageMediaType(image.MediaType) } });
                    break;
                case ClaudeText text:
                    content.Add(new TextBlockParam { Text = text.Text });
                    break;
            }
        }

        return new MessageCreateParams
        {
            Model = feature.Model,
            MaxTokens = feature.MaxTokens,
            System = system,
            Messages = [new() { Role = Role.User, Content = content }],
            OutputConfig = new OutputConfig
            {
                Effort = ParseEffort(feature.Effort),
                Format = request.OutputSchema is null ? null : new JsonOutputFormat { Schema = request.OutputSchema.ToDictionary() },
            },
        };
    }

    /// <summary>
    /// A multi-turn request with tools. Tools render first and the system blocks after them, so the breakpoint on the
    /// last cached system block covers both; top-level automatic caching then extends the cache along the
    /// conversation as tool results are added, so each turn re-reads the previous turns from cache.
    /// </summary>
    internal static MessageCreateParams BuildConversationParameters(ClaudeConversation conversation, AiFeatureOptions feature)
    {
        var system = conversation.System
            .Select(block => new TextBlockParam { Text = block.Text, CacheControl = block.CacheAfter ? new CacheControlEphemeral() : null })
            .ToList();

        var tools = conversation.Tools
            .Select(tool => (ToolUnion)new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = new() { Properties = tool.Properties.ToDictionary(), Required = [.. tool.Required] },
            })
            .ToList();

        var messages = conversation.Messages.Select(message => new MessageParam
        {
            Role = message.FromAssistant ? Role.Assistant : Role.User,
            Content = message.Blocks.Select(ToParam).ToList(),
        }).ToList();

        return new MessageCreateParams
        {
            Model = feature.Model,
            MaxTokens = feature.MaxTokens,
            System = system,
            Tools = tools,
            Messages = messages,
            CacheControl = new CacheControlEphemeral(),
            OutputConfig = new OutputConfig { Effort = ParseEffort(feature.Effort) },
        };
    }

    /// <summary>Echoes a block back in a later request. Thinking signatures must be passed back unchanged.</summary>
    private static ContentBlockParam ToParam(ClaudeBlock block) => block switch
    {
        ClaudeTextBlock text => new TextBlockParam { Text = text.Text },
        ClaudeThinkingBlock thinking => new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature },
        ClaudeRedactedThinkingBlock redacted => new RedactedThinkingBlockParam { Data = redacted.Data },
        ClaudeToolUseBlock toolUse => new ToolUseBlockParam { ID = toolUse.Id, Name = toolUse.Name, Input = toolUse.Input.ToDictionary() },
        ClaudeToolResultBlock result => new ToolResultBlockParam { ToolUseID = result.ToolUseId, Content = result.Content, IsError = result.IsError },
        _ => throw new ArgumentOutOfRangeException(nameof(block), block.GetType().Name, "Unknown content block."),
    };

    private static Effort ParseEffort(string value) => value.ToUpperInvariant() switch
    {
        "LOW" => Effort.Low,
        "HIGH" => Effort.High,
        "MAX" => Effort.Max,
        _ => Effort.Medium,
    };

    private static MediaType ImageMediaType(string mediaType) => mediaType switch
    {
        "image/png" => MediaType.ImagePng,
        "image/gif" => MediaType.ImageGif,
        "image/webp" => MediaType.ImageWebP,
        _ => MediaType.ImageJpeg,
    };

    private static AiServiceException Translate(Exception exception) => exception switch
    {
        AnthropicRateLimitException => new("Claude is busy right now. Wait a minute and try again.", 429, exception),
        AnthropicUnauthorizedException or AnthropicForbiddenException =>
            new("The Claude API key was rejected. An administrator needs to check it.", 503, exception),
        Anthropic5xxException => new("Claude is temporarily unavailable. Try again shortly.", 503, exception),
        AnthropicIOException => new("Couldn't reach the Claude API. Check the server's internet connection.", 503, exception),
        AnthropicBadRequestException => new("Claude couldn't process this input. It may be too large or in an unsupported format.", StatusCodesBadGateway, exception),
        _ => new("The AI request failed. Try again in a moment.", StatusCodesBadGateway, exception),
    };

    private static int ElapsedMs(long started) => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string Shorten(string value) => value.Length <= 500 ? value : value[..500];
}
