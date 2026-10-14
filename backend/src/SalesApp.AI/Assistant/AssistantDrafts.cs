using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SalesApp.AI.Claude;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Assistant;

/// <summary>
/// Turns a create_draft call into a saved draft. The recipient's email and phone come from the customer record,
/// so a draft can't be addressed anywhere else. Invoice and order numbers must exist and belong to the customer,
/// and exact amounts must appear in what the assistant looked up; anything else becomes a warning on the draft.
/// </summary>
public sealed class AssistantDrafts(AppDbContext db, TimeProvider time)
{
    private const int MaxWarnings = 6;

    public async Task<(ToolOutcome Outcome, AssistantDraftDto? Draft)> CreateAsync(
        Guid conversationId, ClaudeToolUseBlock call, IEnumerable<string> toolResultsSoFar, CancellationToken cancellationToken)
    {
        var input = call.Input;
        var kind = AssistantTools.Text(input, "kind").ToLowerInvariant() switch
        {
            "email" => AiDraftKind.Email,
            "whatsapp" => AiDraftKind.WhatsApp,
            _ => (AiDraftKind?)null,
        };
        var body = AssistantTools.Text(input, "body");
        var subject = AssistantTools.Text(input, "subject");
        var purpose = AssistantTools.Text(input, "purpose") is var p && AssistantPrompt.DraftPurposes.Contains(p) ? p : "other";
        var customerId = AssistantTools.Number(input, "customerId");

        if (kind is null)
        {
            return (Error("kind must be \"email\" or \"whatsapp\"."), null);
        }

        if (body.Length == 0 || body.Length > 5000)
        {
            return (Error("The body must be between 1 and 5,000 characters."), null);
        }

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.Id, c.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return (Error($"No customer has id {customerId}. Use {AssistantPrompt.SearchCustomers} to find the right id."), null);
        }

        var warnings = new List<string>();
        var numbers = DraftChecker.DocumentNumbers($"{subject}\n{body}");
        if (numbers.Count > 0)
        {
            var invoices = await db.Invoices.AsNoTracking().Where(i => numbers.Contains(i.Number))
                .Select(i => new { i.Number, i.CustomerId, i.CustomerName }).ToListAsync(cancellationToken);
            var orders = await db.SalesOrders.AsNoTracking()
                .Where(o => numbers.Contains(o.Number) || (o.QuotationNumber != null && numbers.Contains(o.QuotationNumber)))
                .Select(o => new { o.Number, o.QuotationNumber, o.CustomerId, CustomerName = o.Customer!.Name }).ToListAsync(cancellationToken);

            foreach (var number in numbers)
            {
                var owner = invoices.Where(i => i.Number == number).Select(i => (i.CustomerId, i.CustomerName))
                    .Concat(orders.Where(o => o.Number == number || o.QuotationNumber == number).Select(o => (o.CustomerId, o.CustomerName)))
                    .Cast<(int CustomerId, string CustomerName)?>()
                    .FirstOrDefault();
                if (owner is null)
                {
                    warnings.Add($"{number} isn't an invoice, order or quotation in SalesApp.");
                }
                else if (owner.Value.CustomerId != customer.Id)
                {
                    warnings.Add($"{number} belongs to {owner.Value.CustomerName}, not {customer.Name}.");
                }
            }
        }

        foreach (var amount in DraftChecker.UnknownAmounts(body, DraftChecker.NumbersIn(toolResultsSoFar)))
        {
            warnings.Add($"{amount} doesn't match any amount the assistant looked up.");
        }

        warnings = [.. warnings.Take(MaxWarnings)];
        var draft = new AiDraft
        {
            ConversationId = conversationId,
            ToolUseId = call.Id,
            Kind = kind.Value,
            Purpose = purpose,
            CustomerId = customer.Id,
            Subject = kind == AiDraftKind.Email && subject.Length > 0 ? (subject.Length > 200 ? subject[..200] : subject) : null,
            Body = body,
            WarningsJson = JsonSerializer.Serialize(warnings),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.AiDrafts.Add(draft);

        var dto = (await ToDtosAsync([draft], cancellationToken))[0];
        var channel = kind == AiDraftKind.Email ? "email" : "WhatsApp message";
        var contact = kind == AiDraftKind.Email ? dto.Email : dto.Phone;
        var result = new
        {
            shown = true,
            note = $"The {channel} to {customer.Name} is shown to the user with buttons to copy it or open it in their own app. Nothing was sent.",
            recipientOnFile = contact is not null,
            warnings,
        };
        var label = $"Drafted a {channel} to {customer.Name}";
        return (new ToolOutcome(JsonSerializer.Serialize(result, JsonSerializerOptions.Web), false, label), dto);
    }

    /// <summary>Drafts with the customer's current contact details, for the conversation view.</summary>
    public async Task<List<AssistantDraftDto>> ToDtosAsync(IReadOnlyList<AiDraft> drafts, CancellationToken cancellationToken)
    {
        var ids = drafts.Select(d => d.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.ContactPerson, c.Email, c.Phone })
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return [.. drafts.Select(d =>
        {
            var customer = customers.GetValueOrDefault(d.CustomerId);
            return new AssistantDraftDto(d.ToolUseId, d.Kind, d.Purpose, d.CustomerId, customer?.Name ?? "(deleted customer)",
                customer?.ContactPerson, Blank(customer?.Email), Blank(customer?.Phone), d.Subject, d.Body,
                JsonSerializer.Deserialize<List<string>>(d.WarningsJson) ?? []);
        })];
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ToolOutcome Error(string message) => new(message, true, "Couldn't write the draft");
}
