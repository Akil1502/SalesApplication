using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesApp.AI.Claude;

/// <summary>
/// One request to Claude, independent of the SDK so features can be tested against a fake gateway.
/// System blocks come first and should be stable; <see cref="ClaudeSystemBlock.CacheAfter"/> marks the end of the
/// part worth caching. Anything that changes per request belongs in <see cref="Content"/>.
/// </summary>
public sealed record ClaudeRequest(
    string Feature,
    IReadOnlyList<ClaudeSystemBlock> System,
    IReadOnlyList<ClaudeContent> Content,
    IReadOnlyDictionary<string, JsonElement>? OutputSchema = null);

public sealed record ClaudeSystemBlock(string Text, bool CacheAfter = false);

public abstract record ClaudeContent;

public sealed record ClaudeText(string Text) : ClaudeContent;

/// <summary>A PDF, sent whole; Claude reads both its text and its page images.</summary>
public sealed record ClaudePdf(byte[] Data) : ClaudeContent;

/// <summary>A JPEG, PNG, GIF or WebP image.</summary>
public sealed record ClaudeImage(byte[] Data, string MediaType) : ClaudeContent;

public sealed record ClaudeUsage(
    string Model,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheWriteTokens,
    decimal CostUsd,
    int DurationMs);

public sealed record ClaudeResponse(string Text, ClaudeUsage Usage, string? MessageId);

// ---- Tool-using conversations ----------------------------------------------------------------

/// <summary>A tool Claude may call: its name, what it's for, and a JSON schema for its input.</summary>
public sealed record ClaudeTool(string Name, string Description, IReadOnlyDictionary<string, JsonElement> Properties, IReadOnlyList<string> Required);

/// <summary>
/// A content block of a conversation message. The type names match the API's, and the JSON form is what the
/// assistant stores, so a saved conversation replays exactly what Claude saw.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ClaudeTextBlock), "text")]
[JsonDerivedType(typeof(ClaudeThinkingBlock), "thinking")]
[JsonDerivedType(typeof(ClaudeRedactedThinkingBlock), "redacted_thinking")]
[JsonDerivedType(typeof(ClaudeToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ClaudeToolResultBlock), "tool_result")]
public abstract record ClaudeBlock;

public sealed record ClaudeTextBlock(string Text) : ClaudeBlock;

/// <summary>Claude's reasoning. Must be sent back unchanged (signature included) in the next turn.</summary>
public sealed record ClaudeThinkingBlock(string Thinking, string Signature) : ClaudeBlock;

public sealed record ClaudeRedactedThinkingBlock(string Data) : ClaudeBlock;

public sealed record ClaudeToolUseBlock(string Id, string Name, IReadOnlyDictionary<string, JsonElement> Input) : ClaudeBlock;

public sealed record ClaudeToolResultBlock(string ToolUseId, string Content, bool IsError = false) : ClaudeBlock;

public sealed record ClaudeMessage(bool FromAssistant, IReadOnlyList<ClaudeBlock> Blocks);

/// <summary>
/// A conversation so far. System blocks and tools must stay identical between turns (they're the cached prefix);
/// each turn appends Claude's reply and the tool results to <see cref="Messages"/>.
/// </summary>
public sealed record ClaudeConversation(
    string Feature,
    IReadOnlyList<ClaudeSystemBlock> System,
    IReadOnlyList<ClaudeTool> Tools,
    IReadOnlyList<ClaudeMessage> Messages);

public sealed record ClaudeTurn(IReadOnlyList<ClaudeBlock> Blocks, string StopReason, ClaudeUsage Usage, string? MessageId);

/// <summary>Stores and reloads message content. Round-trips every block unchanged, thinking signatures included.</summary>
public static class ClaudeBlockJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<ClaudeBlock> blocks) => JsonSerializer.Serialize(blocks, Options);

    public static IReadOnlyList<ClaudeBlock> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ClaudeBlock>>(json, Options) ?? [];
}

// ---- Streaming -------------------------------------------------------------------------------

/// <summary>Something worth showing while a reply streams.</summary>
public abstract record ClaudeStreamEvent;

/// <summary>More of Claude's reply text.</summary>
public sealed record ClaudeTextDelta(string Text) : ClaudeStreamEvent;

/// <summary>Claude started reasoning (the reasoning itself isn't returned).</summary>
public sealed record ClaudeThinkingStarted : ClaudeStreamEvent;

/// <summary>Claude started writing a tool call; its input follows and is complete when the turn ends.</summary>
public sealed record ClaudeToolCallStarted(string Id, string Name) : ClaudeStreamEvent;
