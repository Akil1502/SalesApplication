namespace SalesApp.Core.Entities;

public enum AiOutcome
{
    Success,

    /// <summary>Claude declined the request (stop reason "refusal"). Billed like any other call.</summary>
    Refused,

    /// <summary>The reply hit the output-token limit and was cut off.</summary>
    Truncated,

    /// <summary>The call failed: network, rate limit, invalid key, or an unreadable reply.</summary>
    Failed,
}

/// <summary>
/// One Claude API call: who made it, for which feature, the tokens it used and what it cost. Written for every
/// call, successful or not, so usage and spend can be audited and the per-user daily budget enforced.
/// </summary>
public sealed class AiUsageLog
{
    public long Id { get; set; }

    /// <summary>The app feature that made the call, such as "OrderEntry".</summary>
    public required string Feature { get; set; }
    public required string Model { get; set; }

    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int DurationMs { get; set; }

    /// <summary>Input tokens billed at the full rate (the part of the prompt that wasn't cached).</summary>
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }

    /// <summary>Estimated from the configured price list; the Anthropic console has the authoritative bill.</summary>
    public decimal CostUsd { get; set; }

    public AiOutcome Outcome { get; set; }
    public string? Error { get; set; }

    /// <summary>Anthropic's message id, for matching a call against their console or support.</summary>
    public string? RequestId { get; set; }
}

/// <summary>
/// Another name customers use for a product ("30s white", "carded 30"), learned when someone corrects smart order
/// entry. Aliases go into the catalog Claude matches against, so the same wording is recognised next time.
/// </summary>
public sealed class ProductAlias
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>The wording as it appeared in the order.</summary>
    public required string Alias { get; set; }

    /// <summary>Lower case with punctuation and extra spaces removed; unique, so one wording maps to one product.</summary>
    public required string NormalizedAlias { get; set; }

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum AiQueryOutcome
{
    Succeeded,

    /// <summary>The SQL checker refused the query before it reached the database.</summary>
    Rejected,

    /// <summary>The database refused or failed the query (permission, syntax, timeout).</summary>
    Failed,
}

/// <summary>
/// One SQL query the AI tried while answering a question: what it ran, whether the checker or the database refused
/// it, and how many rows came back. Kept for auditing the "ask your data" feature.
/// </summary>
public sealed class AiQueryLog
{
    public long Id { get; set; }

    /// <summary>Groups the queries made while answering one question.</summary>
    public Guid RunId { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public required string Question { get; set; }
    public required string Sql { get; set; }

    /// <summary>The database user the query ran as: ai_reader, or ai_reader_mgr for managers.</summary>
    public required string RunAs { get; set; }

    public AiQueryOutcome Outcome { get; set; }
    public string? Error { get; set; }
    public int RowCount { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A chat with the assistant. The system prompt is stored with it and reused for every turn, because Claude's
/// saved reasoning is only valid while the start of the conversation (system prompt, tools, earlier messages)
/// stays byte-for-byte the same.
/// </summary>
public sealed class AiConversation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public required string Title { get; set; }

    /// <summary>The system prompt as first sent; later turns send exactly this text.</summary>
    public required string SystemPrompt { get; set; }

    /// <summary>Version of the tool set the conversation started with. A different version changes the prefix.</summary>
    public int ToolsVersion { get; set; }

    /// <summary>Tokens in the conversation at the last call (input + cache read + cache write); limits how long it can grow.</summary>
    public int ContextTokens { get; set; }

    public decimal CostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<AiMessage> Messages { get; set; } = [];
}

public enum AiMessageRole
{
    User,
    Assistant,
}

/// <summary>Whether an assistant message is Claude's whole reply or what had streamed when it stopped.</summary>
public enum AiMessageStatus
{
    Complete,
    Stopped,
    Failed,
}

/// <summary>
/// One message of a conversation, exactly as sent to or received from Claude (content blocks as JSON, thinking
/// signatures included). Append-only: a database trigger rejects updates, so the history replayed to Claude is
/// always what it saw before.
/// </summary>
public sealed class AiMessage
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }

    /// <summary>Position in the conversation, from 1.</summary>
    public int Sequence { get; set; }

    public AiMessageRole Role { get; set; }
    public required string ContentJson { get; set; }

    /// <summary>
    /// What the conversation view shows for this message beyond its content (the labels of the lookups whose results
    /// it carries), so a reloaded conversation looks like it did live. Never sent to Claude.
    /// </summary>
    public string? DisplayJson { get; set; }

    public AiMessageStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum AiDraftKind
{
    Email,
    WhatsApp,
}

/// <summary>
/// A message the assistant drafted for the user to send. The recipient comes from the customer record, never from
/// the model, and the app never sends it: the user copies it or opens it in their own email or WhatsApp.
/// </summary>
public sealed class AiDraft
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }

    /// <summary>The tool call that created it, so the conversation view can put it in place.</summary>
    public required string ToolUseId { get; set; }

    public AiDraftKind Kind { get; set; }
    public required string Purpose { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string? Subject { get; set; }
    public required string Body { get; set; }

    /// <summary>Problems the checks found (an invoice number that isn't this customer's, an amount not in the data), as JSON.</summary>
    public required string WarningsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
