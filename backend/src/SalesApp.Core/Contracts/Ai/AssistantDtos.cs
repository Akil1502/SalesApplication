using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Entities;

namespace SalesApp.Core.Contracts.Ai;

public sealed record AssistantConversationSummaryDto(Guid Id, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record AssistantSendRequest
{
    /// <summary>The conversation to continue; omitted to start a new one.</summary>
    public Guid? ConversationId { get; init; }

    [Required(ErrorMessage = "Type a message."), MaxLength(4000, ErrorMessage = "Keep the message under 4,000 characters.")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>What one entry of the conversation view shows.</summary>
public enum AssistantItemKind
{
    User,
    Assistant,
    Tool,
    Draft,
}

/// <summary>A lookup the assistant made. <see cref="Succeeded"/> is null while it runs.</summary>
public sealed record AssistantToolDto(string Id, string Name, string Label, bool? Succeeded, string? Detail);

/// <summary>
/// A drafted email or WhatsApp message. Email and phone come from the customer record. Warnings list invoice
/// numbers that aren't this customer's and amounts that weren't in the data the assistant looked up.
/// </summary>
public sealed record AssistantDraftDto(
    string ToolUseId,
    AiDraftKind Kind,
    string Purpose,
    int CustomerId,
    string CustomerName,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Subject,
    string Body,
    IReadOnlyList<string> Warnings);

/// <summary>One entry of the conversation view. <see cref="Interrupted"/> marks a reply that was stopped or failed part-way.</summary>
public sealed record AssistantItemDto(
    AssistantItemKind Kind,
    string? Text,
    AssistantToolDto? Tool,
    AssistantDraftDto? Draft,
    bool Interrupted);

/// <summary>
/// A saved conversation. <see cref="Notice"/> explains when it can't be continued (it has grown too long), so the
/// user starts a new one.
/// </summary>
public sealed record AssistantConversationDto(
    Guid Id,
    string Title,
    IReadOnlyList<AssistantItemDto> Items,
    string? Notice,
    decimal CostInr,
    DateTime UpdatedAtUtc);

// Server-sent events while a reply streams: "conversation" (new conversations only), "text", "tool", "draft",
// then "done" or "error".

public sealed record AssistantStartedEvent(Guid ConversationId, string Title);

public sealed record AssistantTextEvent(string Delta);

public sealed record AssistantDoneEvent(AiUsageDto Usage, string? Notice);
