using System.Text.Json;

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
