using System.Text;
using System.Text.Json;
using Anthropic.Models.Messages;

namespace SalesApp.AI.Claude;

/// <summary>
/// Rebuilds a complete assistant turn from streamed events: text from text deltas, each tool call's input from its
/// partial-JSON deltas, and thinking blocks with their signatures (which must go back to Claude unchanged). Usage
/// comes from message_start and is updated by message_delta. Returns the events worth showing as they arrive.
/// </summary>
internal sealed class ClaudeStreamAccumulator
{
    private readonly SortedDictionary<long, BlockBuilder> _blocks = [];

    public string? MessageId { get; private set; }
    public string? StopReason { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public long CacheReadTokens { get; private set; }
    public long CacheWriteTokens { get; private set; }

    /// <summary>The text streamed so far, for keeping what the user saw when a reply is stopped.</summary>
    public string Text => string.Concat(_blocks.Values.OfType<TextBuilder>().Select(b => b.Text.ToString()));

    public ClaudeStreamEvent? Add(RawMessageStreamEvent streamEvent)
    {
        if (streamEvent.TryPickStart(out var start))
        {
            MessageId = start.Message.ID;
            var usage = start.Message.Usage;
            InputTokens = usage.InputTokens;
            OutputTokens = usage.OutputTokens;
            CacheReadTokens = usage.CacheReadInputTokens ?? 0;
            CacheWriteTokens = usage.CacheCreationInputTokens ?? 0;
            return null;
        }

        if (streamEvent.TryPickContentBlockStart(out var blockStart))
        {
            var block = blockStart.ContentBlock;
            if (block.TryPickText(out var text))
            {
                _blocks[blockStart.Index] = new TextBuilder(text.Text);
                return text.Text.Length > 0 ? new ClaudeTextDelta(text.Text) : null;
            }

            if (block.TryPickThinking(out var thinking))
            {
                _blocks[blockStart.Index] = new ThinkingBuilder(thinking.Thinking, thinking.Signature);
                return new ClaudeThinkingStarted();
            }

            if (block.TryPickRedactedThinking(out var redacted))
            {
                _blocks[blockStart.Index] = new RedactedBuilder(redacted.Data);
                return null;
            }

            if (block.TryPickToolUse(out var toolUse))
            {
                _blocks[blockStart.Index] = new ToolUseBuilder(toolUse.ID, toolUse.Name);
                return new ClaudeToolCallStarted(toolUse.ID, toolUse.Name);
            }

            // The app declares no server tools, so no other block types can arrive.
            throw new AiServiceException($"Claude sent an unexpected content block ({block.Type}).", 502);
        }

        if (streamEvent.TryPickContentBlockDelta(out var blockDelta) && _blocks.TryGetValue(blockDelta.Index, out var builder))
        {
            var delta = blockDelta.Delta;
            if (delta.TryPickText(out var text) && builder is TextBuilder textBuilder)
            {
                textBuilder.Text.Append(text.Text);
                return new ClaudeTextDelta(text.Text);
            }

            if (delta.TryPickInputJson(out var json) && builder is ToolUseBuilder toolBuilder)
            {
                toolBuilder.Json.Append(json.PartialJson);
            }
            else if (delta.TryPickThinking(out var thinking) && builder is ThinkingBuilder thinkingBuilder)
            {
                thinkingBuilder.Thinking.Append(thinking.Thinking);
            }
            else if (delta.TryPickSignature(out var signature) && builder is ThinkingBuilder signedBuilder)
            {
                signedBuilder.Signature.Append(signature.Signature);
            }

            return null;
        }

        if (streamEvent.TryPickDelta(out var messageDelta))
        {
            StopReason = messageDelta.Delta.StopReason?.Raw() ?? StopReason;
            var usage = messageDelta.Usage;
            OutputTokens = usage.OutputTokens;
            InputTokens = usage.InputTokens ?? InputTokens;
            CacheReadTokens = usage.CacheReadInputTokens ?? CacheReadTokens;
            CacheWriteTokens = usage.CacheCreationInputTokens ?? CacheWriteTokens;
        }

        return null;
    }

    /// <summary>The finished turn's blocks, in the order Claude wrote them.</summary>
    public IReadOnlyList<ClaudeBlock> Blocks() => [.. _blocks.Values.Select(b => b.Build())];

    private abstract class BlockBuilder
    {
        public abstract ClaudeBlock Build();
    }

    private sealed class TextBuilder(string initial) : BlockBuilder
    {
        public StringBuilder Text { get; } = new(initial);

        public override ClaudeBlock Build() => new ClaudeTextBlock(Text.ToString());
    }

    private sealed class ThinkingBuilder(string thinking, string signature) : BlockBuilder
    {
        public StringBuilder Thinking { get; } = new(thinking);
        public StringBuilder Signature { get; } = new(signature);

        public override ClaudeBlock Build() => new ClaudeThinkingBlock(Thinking.ToString(), Signature.ToString());
    }

    private sealed class RedactedBuilder(string data) : BlockBuilder
    {
        public override ClaudeBlock Build() => new ClaudeRedactedThinkingBlock(data);
    }

    private sealed class ToolUseBuilder(string id, string name) : BlockBuilder
    {
        public StringBuilder Json { get; } = new();

        public override ClaudeBlock Build()
        {
            // A tool call with no input streams no deltas at all.
            var json = Json.Length == 0 ? "{}" : Json.ToString();
            Dictionary<string, JsonElement>? input;
            try
            {
                input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            }
            catch (JsonException exception)
            {
                throw new AiServiceException($"Claude's call to {name} arrived incomplete. Try again.", 502, exception);
            }

            return new ClaudeToolUseBlock(id, name, input ?? []);
        }
    }
}
