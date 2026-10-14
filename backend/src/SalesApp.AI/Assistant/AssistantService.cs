using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesApp.AI.Claude;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Assistant;

/// <summary>Settings for the assistant.</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Ai:Assistant";

    /// <summary>Most rounds of lookups Claude may make for one message before it must answer.</summary>
    public int MaxToolRounds { get; set; } = 8;

    /// <summary>
    /// A conversation stops taking messages once it holds this many tokens: every turn re-sends the whole history,
    /// so a very long chat gets slower and dearer. The user starts a new one.
    /// </summary>
    public int MaxContextTokens { get; set; } = 100_000;
}

/// <summary>Where a streaming reply goes: the controller writes these as server-sent events.</summary>
public interface IAssistantEvents
{
    Task StartedAsync(AssistantStartedEvent started);
    Task TextAsync(string delta);
    Task ToolAsync(AssistantToolDto tool);
    Task DraftAsync(AssistantDraftDto draft);
    Task DoneAsync(AssistantDoneEvent done);
}

public interface IAssistantService
{
    Task<IReadOnlyList<AssistantConversationSummaryDto>> ListAsync(CancellationToken cancellationToken);
    Task<AssistantConversationDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task SendAsync(AssistantSendRequest request, IAssistantEvents events, CancellationToken cancellationToken);
}

/// <summary>
/// The assistant's conversations. Each message is stored as it happens, exactly as sent to or received from Claude,
/// and never changed: the user's message straight away, each reply when it's complete (or what had streamed, if the
/// user pressed Stop), and a reply's tool calls together with their results. Every turn sends the stored history
/// unchanged after the stored system prompt, so the prompt cache keeps working and Claude's saved reasoning stays
/// valid.
/// </summary>
public sealed class AssistantService(
    IClaudeGateway claude,
    AssistantTools tools,
    AssistantDrafts drafts,
    AppDbContext db,
    ICurrentUser currentUser,
    IBusinessClock clock,
    TimeProvider time,
    IOptionsMonitor<AiOptions> aiOptions,
    IOptionsMonitor<AssistantOptions> options) : IAssistantService
{
    public const string Feature = "Assistant";
    private const int ListLimit = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One reply at a time per conversation (a second tab would otherwise interleave messages).
    private static readonly ConcurrentDictionary<Guid, byte> Busy = new();

    public async Task<IReadOnlyList<AssistantConversationSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.AiConversations.AsNoTracking()
            .Where(c => c.UserId == currentUser.UserId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Take(ListLimit)
            .Select(c => new AssistantConversationSummaryDto(c.Id, c.Title, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(id, cancellationToken);
        db.AiConversations.Remove(conversation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AssistantConversationDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(id, cancellationToken);
        var messages = await db.AiMessages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);
        var saved = await db.AiDrafts.AsNoTracking().Where(d => d.ConversationId == id).ToListAsync(cancellationToken);
        var draftsByCall = (await drafts.ToDtosAsync(saved, cancellationToken)).ToDictionary(d => d.ToolUseId);

        return new AssistantConversationDto(conversation.Id, conversation.Title, Items(messages, draftsByCall), Notice(conversation),
            Math.Round(conversation.CostUsd * aiOptions.CurrentValue.UsdToInr, 2), conversation.UpdatedAtUtc);
    }

    public async Task SendAsync(AssistantSendRequest request, IAssistantEvents events, CancellationToken cancellationToken)
    {
        var text = request.Message.Trim();
        if (text.Length == 0)
        {
            throw new ValidationException("message", "Type a message.");
        }

        if (!claude.IsAvailable)
        {
            throw new AiUnavailableException("The assistant needs AI to be switched on and a Claude API key.");
        }

        var conversation = request.ConversationId is { } id ? await FindAsync(id, cancellationToken) : null;
        if (conversation is not null && conversation.ContextTokens >= Limit)
        {
            throw new ConflictException(Notice(conversation)!);
        }

        if (conversation is null)
        {
            conversation = await StartAsync(text, cancellationToken);
            await events.StartedAsync(new AssistantStartedEvent(conversation.Id, conversation.Title));
        }

        if (!Busy.TryAdd(conversation.Id, 0))
        {
            throw new ConflictException("This conversation is already answering in another window. Wait for it to finish.");
        }

        try
        {
            var history = (await db.AiMessages.AsNoTracking()
                    .Where(m => m.ConversationId == conversation.Id)
                    .OrderBy(m => m.Sequence)
                    .ToListAsync(cancellationToken))
                .Select(m => new ClaudeMessage(m.Role == AiMessageRole.Assistant, ClaudeBlockJson.Deserialize(m.ContentJson)))
                .ToList();
            await ReplyAsync(conversation, history, text, events, cancellationToken);
        }
        finally
        {
            Busy.TryRemove(conversation.Id, out _);
        }
    }

    private int Limit => Math.Max(10_000, options.CurrentValue.MaxContextTokens);

    private async Task ReplyAsync(AiConversation conversation, List<ClaudeMessage> history, string text, IAssistantEvents events, CancellationToken cancellationToken)
    {
        var sequence = history.Count;
        var userMessage = new ClaudeMessage(false, [new ClaudeTextBlock(AssistantPrompt.DateLine(clock.Today)), new ClaudeTextBlock(text)]);
        history.Add(userMessage);
        Append(conversation, ++sequence, userMessage, AiMessageStatus.Complete);
        conversation.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(CancellationToken.None);

        // A conversation that began with a different tool set no longer matches the prefix its reasoning was made
        // with. Its thinking blocks are left out of the request (not out of storage), which the API accepts.
        var stripThinking = conversation.ToolsVersion != AssistantPrompt.ToolsVersion;
        var system = new[] { new ClaudeSystemBlock(conversation.SystemPrompt, CacheAfter: true) };
        var usage = new List<ClaudeUsage>();
        var maxRounds = Math.Clamp(options.CurrentValue.MaxToolRounds, 1, 20);

        for (var round = 0; ; round++)
        {
            var streamed = new StringBuilder();
            List<ClaudeMessage> sent = stripThinking ? [.. history.Select(WithoutThinking)] : history;
            ClaudeTurn turn;
            try
            {
                turn = await claude.StreamTurnAsync(new ClaudeConversation(Feature, system, AssistantPrompt.Tools, sent), async shown =>
                {
                    if (shown is ClaudeTextDelta delta)
                    {
                        streamed.Append(delta.Text);
                        await events.TextAsync(delta.Text);
                    }
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or AiServiceException or AiRefusedException or AiBudgetExceededException or AiUnavailableException)
            {
                // Keep what the user already saw. Only the text: an unfinished tool call can't be replayed.
                if (streamed.Length > 0)
                {
                    Append(conversation, ++sequence, new ClaudeMessage(true, [new ClaudeTextBlock(streamed.ToString())]),
                        exception is OperationCanceledException ? AiMessageStatus.Stopped : AiMessageStatus.Failed);
                }

                await FinishAsync(conversation, lastTurn: null);
                throw;
            }

            usage.Add(turn.Usage);
            var calls = turn.Blocks.OfType<ClaudeToolUseBlock>().ToList();
            var reply = new ClaudeMessage(true, turn.Blocks);
            history.Add(reply);

            if (calls.Count == 0)
            {
                Append(conversation, ++sequence, reply, AiMessageStatus.Complete);
                await FinishAsync(conversation, turn.Usage);
                await events.DoneAsync(new AssistantDoneEvent(Total(usage), Notice(conversation)));
                return;
            }

            // Run the lookups, then store the reply and its results together, so the history never holds a tool
            // call without its result. Lookups are quick and read-only; they finish even if the user presses Stop.
            var results = new List<ClaudeBlock>();
            var labels = new List<AssistantToolDto>();
            var outOfRounds = round >= maxRounds;
            foreach (var call in calls)
            {
                ToolOutcome outcome;
                AssistantDraftDto? draft = null;
                if (outOfRounds)
                {
                    outcome = new ToolOutcome("The limit of lookups for one message has been reached. Answer with what you have.", true, "Too many lookups");
                }
                else if (call.Name == AssistantPrompt.CreateDraft)
                {
                    (outcome, draft) = await drafts.CreateAsync(conversation.Id, call, ToolResults(history), CancellationToken.None);
                }
                else
                {
                    await Emit(() => events.ToolAsync(new AssistantToolDto(call.Id, call.Name, AssistantTools.RunningLabel(call.Name, call.Input), null, null)));
                    outcome = await tools.RunAsync(conversation.Id, call.Name, call.Input, CancellationToken.None);
                }

                results.Add(new ClaudeToolResultBlock(call.Id, outcome.Content, outcome.IsError));
                var label = new AssistantToolDto(call.Id, call.Name, outcome.Label, !outcome.IsError, outcome.Detail);
                labels.Add(label);
                await Emit(() => draft is null ? events.ToolAsync(label) : events.DraftAsync(draft));
            }

            if (round == maxRounds - 1)
            {
                // Tools can't be removed mid-conversation (that would change the prefix), so the limit is a note.
                results.Add(new ClaudeTextBlock("This is the last round of lookups for this message: answer now with what you have."));
            }

            var toolMessage = new ClaudeMessage(false, results);
            history.Add(toolMessage);
            Append(conversation, ++sequence, reply, AiMessageStatus.Complete);
            Append(conversation, ++sequence, toolMessage, AiMessageStatus.Complete, JsonSerializer.Serialize(new ToolDisplay(labels), Json));
            await FinishAsync(conversation, turn.Usage);

            if (outOfRounds)
            {
                throw new AiServiceException("The assistant needed too many lookups for this message. Try asking something more specific.", 502);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        async Task Emit(Func<Task> write)
        {
            // After Stop the browser has gone; the lookups still finish and are stored, but there's no one to tell.
            if (!cancellationToken.IsCancellationRequested)
            {
                await write();
            }
        }
    }

    private async Task<AiConversation> StartAsync(string firstMessage, CancellationToken cancellationToken)
    {
        var company = await db.CompanySettings.AsNoTracking().Select(s => s.TradeName ?? s.LegalName).FirstOrDefaultAsync(cancellationToken) ?? "the company";
        var userName = await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken) ?? "the user";
        var role = currentUser.IsInRole(Roles.Admin) ? "administrator" : currentUser.IsInRole(Roles.SalesManager) ? "sales manager" : "sales rep";
        var now = time.GetUtcNow().UtcDateTime;
        var title = string.Join(' ', firstMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.UserId,
            Title = title.Length > 80 ? title[..77] + "…" : title,
            SystemPrompt = AssistantPrompt.System(company, userName, role, currentUser.IsManagerOrAdmin),
            ToolsVersion = AssistantPrompt.ToolsVersion,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.AiConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    private void Append(AiConversation conversation, int sequence, ClaudeMessage message, AiMessageStatus status, string? display = null) =>
        db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id,
            Sequence = sequence,
            Role = message.FromAssistant ? AiMessageRole.Assistant : AiMessageRole.User,
            ContentJson = ClaudeBlockJson.Serialize(message.Blocks),
            DisplayJson = display,
            Status = status,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        });

    /// <summary>
    /// Saves what's pending, adding the turn just completed to the conversation's cost and size. Not cancellable: it
    /// records work already done. (A call stopped part-way is in the usage log but not in the conversation's cost.)
    /// </summary>
    private async Task FinishAsync(AiConversation conversation, ClaudeUsage? lastTurn)
    {
        if (lastTurn is not null)
        {
            conversation.CostUsd += lastTurn.CostUsd;
            conversation.ContextTokens = lastTurn.InputTokens + lastTurn.CacheReadTokens + lastTurn.CacheWriteTokens + lastTurn.OutputTokens;
        }

        conversation.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private string? Notice(AiConversation conversation) =>
        conversation.ContextTokens >= Limit
            ? "This conversation has reached its length limit. Start a new conversation to carry on."
            : conversation.ContextTokens >= Limit * 0.8
                ? "This conversation is getting long. Starting a new one soon keeps answers quick and cheap."
                : null;

    private AiUsageDto Total(List<ClaudeUsage> usage)
    {
        var cost = usage.Sum(u => u.CostUsd);
        return new AiUsageDto(usage.LastOrDefault()?.Model ?? aiOptions.CurrentValue.For(Feature).Model,
            usage.Sum(u => u.InputTokens), usage.Sum(u => u.OutputTokens), usage.Sum(u => u.CacheReadTokens), usage.Sum(u => u.CacheWriteTokens),
            cost, Math.Round(cost * aiOptions.CurrentValue.UsdToInr, 2), usage.Sum(u => u.DurationMs));
    }

    private async Task<AiConversation> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.AiConversations.SingleOrDefaultAsync(c => c.Id == id && c.UserId == currentUser.UserId, cancellationToken)
        ?? throw new NotFoundException("That conversation doesn't exist or has been deleted.");

    private static IEnumerable<string> ToolResults(IEnumerable<ClaudeMessage> history) =>
        history.Where(m => !m.FromAssistant).SelectMany(m => m.Blocks).OfType<ClaudeToolResultBlock>().Where(r => !r.IsError).Select(r => r.Content);

    private static ClaudeMessage WithoutThinking(ClaudeMessage message) =>
        message.FromAssistant ? message with { Blocks = [.. message.Blocks.Where(b => b is not (ClaudeThinkingBlock or ClaudeRedactedThinkingBlock))] } : message;

    /// <summary>Labels of the lookups whose results a message carries, as shown live.</summary>
    private sealed record ToolDisplay(IReadOnlyList<AssistantToolDto> Tools);

    /// <summary>Rebuilds the conversation view from the stored messages.</summary>
    private static List<AssistantItemDto> Items(List<AiMessage> messages, Dictionary<string, AssistantDraftDto> draftsByCall)
    {
        var items = new List<AssistantItemDto>();
        var toolIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var blocks = ClaudeBlockJson.Deserialize(message.ContentJson);
            if (message.Role == AiMessageRole.User)
            {
                var results = blocks.OfType<ClaudeToolResultBlock>().ToList();
                if (results.Count == 0)
                {
                    // The user's words are the last text block; the first carries today's date.
                    items.Add(new AssistantItemDto(AssistantItemKind.User, blocks.OfType<ClaudeTextBlock>().LastOrDefault()?.Text ?? "", null, null, false));
                    continue;
                }

                var shown = message.DisplayJson is { } display
                    ? JsonSerializer.Deserialize<ToolDisplay>(display, Json)?.Tools.ToDictionary(t => t.Id) ?? []
                    : [];
                foreach (var result in results)
                {
                    if (toolIndex.TryGetValue(result.ToolUseId, out var index) && items[index].Tool is { } tool)
                    {
                        items[index] = items[index] with
                        {
                            Tool = shown.TryGetValue(result.ToolUseId, out var label) ? label : tool with { Succeeded = !result.IsError },
                        };
                    }
                }

                continue;
            }

            var interrupted = message.Status != AiMessageStatus.Complete;
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case ClaudeTextBlock { Text.Length: > 0 } textBlock:
                        if (items.Count > 0 && items[^1].Kind == AssistantItemKind.Assistant)
                        {
                            items[^1] = items[^1] with { Text = $"{items[^1].Text}\n\n{textBlock.Text}", Interrupted = interrupted };
                        }
                        else
                        {
                            items.Add(new AssistantItemDto(AssistantItemKind.Assistant, textBlock.Text, null, null, interrupted));
                        }

                        break;
                    case ClaudeToolUseBlock call when call.Name == AssistantPrompt.CreateDraft && draftsByCall.TryGetValue(call.Id, out var draft):
                        items.Add(new AssistantItemDto(AssistantItemKind.Draft, null, null, draft, false));
                        break;
                    case ClaudeToolUseBlock call:
                        toolIndex[call.Id] = items.Count;
                        items.Add(new AssistantItemDto(AssistantItemKind.Tool, null,
                            new AssistantToolDto(call.Id, call.Name, AssistantTools.RunningLabel(call.Name, call.Input), null, null), null, false));
                        break;
                }
            }
        }

        return items;
    }
}
