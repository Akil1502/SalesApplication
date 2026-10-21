using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SalesApp.AI.Claude;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Assistant;

/// <summary>
/// Turns a create_draft call into a saved draft. The recipient's email and phone come from the customer or supplier
/// record, so a draft can't be addressed anywhere else. Document numbers must exist and belong to the recipient
/// (invoices, orders and quotations for a customer; purchase orders and bills for a supplier), and exact amounts must
/// appear in what the assistant looked up; anything else becomes a warning on the draft.
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
        var supplierId = AssistantTools.Number(input, "supplierId");

        if (kind is null)
        {
            return (Error("kind must be \"email\" or \"whatsapp\"."), null);
        }

        if (body.Length == 0 || body.Length > 5000)
        {
            return (Error("The body must be between 1 and 5,000 characters."), null);
        }

        if ((customerId > 0) == (supplierId > 0))
        {
            return (Error("Give either a customerId or a supplierId (the other as 0): a draft goes to one customer or one supplier."), null);
        }

        var text = $"{subject}\n{body}";
        var (recipientName, warnings, error) = supplierId > 0
            ? await CheckSupplierAsync(supplierId, text, cancellationToken)
            : await CheckCustomerAsync(customerId, text, cancellationToken);
        if (error is not null)
        {
            return (Error(error), null);
        }

        foreach (var amount in DraftChecker.UnknownAmounts(body, DraftChecker.NumbersIn(toolResultsSoFar)))
        {
            warnings.Add(L.Msg($"{amount} doesn't match any amount the assistant looked up."));
        }

        warnings = [.. warnings.Take(MaxWarnings)];
        var draft = new AiDraft
        {
            ConversationId = conversationId,
            ToolUseId = call.Id,
            Kind = kind.Value,
            Purpose = purpose,
            CustomerId = customerId > 0 ? customerId : null,
            SupplierId = supplierId > 0 ? supplierId : null,
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
            note = $"The {channel} to {recipientName} is shown to the user with buttons to copy it or open it in their own app. Nothing was sent.",
            recipientOnFile = contact is not null,
            warnings,
        };
        var label = kind == AiDraftKind.Email ? L.Msg($"Drafted an email to {recipientName}") : L.Msg($"Drafted a WhatsApp message to {recipientName}");
        return (new ToolOutcome(JsonSerializer.Serialize(result, JsonSerializerOptions.Web), false, label), dto);
    }

    /// <summary>Drafts with the recipient's current contact details, for the conversation view.</summary>
    public async Task<List<AssistantDraftDto>> ToDtosAsync(IReadOnlyList<AiDraft> drafts, CancellationToken cancellationToken)
    {
        var customerIds = drafts.Where(d => d.CustomerId is not null).Select(d => d.CustomerId!.Value).Distinct().ToList();
        var supplierIds = drafts.Where(d => d.SupplierId is not null).Select(d => d.SupplierId!.Value).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new Recipient(c.Id, c.Name, c.ContactPerson, c.Email, c.Phone))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .Select(s => new Recipient(s.Id, s.Name, s.ContactPerson, s.Email, s.Phone))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return [.. drafts.Select(d =>
        {
            var recipient = d.SupplierId is { } supplierId ? suppliers.GetValueOrDefault(supplierId)
                : d.CustomerId is { } customerId ? customers.GetValueOrDefault(customerId)
                : null;
            var missing = d.SupplierId is not null ? "(deleted supplier)" : "(deleted customer)";
            return new AssistantDraftDto(d.ToolUseId, d.Kind, d.Purpose, d.CustomerId, d.SupplierId, recipient?.Name ?? missing,
                recipient?.ContactPerson, Blank(recipient?.Email), Blank(recipient?.Phone), d.Subject, d.Body,
                JsonSerializer.Deserialize<List<string>>(d.WarningsJson) ?? []);
        })];
    }

    private async Task<(string Name, List<string> Warnings, string? Error)> CheckCustomerAsync(int customerId, string text, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => new { c.Id, c.Name }).SingleOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return ("", [], $"No customer has id {customerId}. Use {AssistantPrompt.SearchCustomers} to find the right id.");
        }

        var warnings = new List<string>();
        var numbers = DraftChecker.DocumentNumbers(text);
        if (numbers.Count > 0)
        {
            var invoices = await db.Invoices.AsNoTracking().Where(i => numbers.Contains(i.Number))
                .Select(i => new { i.Number, i.CustomerId, i.CustomerName }).ToListAsync(cancellationToken);
            var orders = await db.SalesOrders.AsNoTracking()
                .Where(o => numbers.Contains(o.Number) || (o.QuotationNumber != null && numbers.Contains(o.QuotationNumber)))
                .Select(o => new { o.Number, o.QuotationNumber, o.CustomerId, CustomerName = o.Customer!.Name }).ToListAsync(cancellationToken);
            var creditNotes = await db.CreditNotes.AsNoTracking().Where(c => numbers.Contains(c.Number))
                .Select(c => new { c.Number, c.CustomerId, c.CustomerName }).ToListAsync(cancellationToken);

            foreach (var number in numbers)
            {
                var owner = invoices.Where(i => i.Number == number).Select(i => (i.CustomerId, i.CustomerName))
                    .Concat(orders.Where(o => o.Number == number || o.QuotationNumber == number).Select(o => (o.CustomerId, o.CustomerName)))
                    .Concat(creditNotes.Where(c => c.Number == number).Select(c => (c.CustomerId, c.CustomerName)))
                    .Cast<(int CustomerId, string CustomerName)?>()
                    .FirstOrDefault();
                if (owner is null)
                {
                    warnings.Add(L.Msg($"{number} isn't an invoice, credit note, order or quotation in SalesApp."));
                }
                else if (owner.Value.CustomerId != customer.Id)
                {
                    warnings.Add(L.Msg($"{number} belongs to {owner.Value.CustomerName}, not {customer.Name}."));
                }
            }
        }

        return (customer.Name, warnings, null);
    }

    private async Task<(string Name, List<string> Warnings, string? Error)> CheckSupplierAsync(int supplierId, string text, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == supplierId).Select(s => new { s.Id, s.Name }).SingleOrDefaultAsync(cancellationToken);
        if (supplier is null)
        {
            return ("", [], $"No supplier has id {supplierId}. Use {AssistantPrompt.SearchSuppliers} to find the right id.");
        }

        var warnings = new List<string>();
        var numbers = DraftChecker.DocumentNumbers(text);
        if (numbers.Count > 0)
        {
            var orders = await db.PurchaseOrders.AsNoTracking().Where(o => numbers.Contains(o.Number))
                .Select(o => new { o.Number, o.SupplierId, SupplierName = o.Supplier!.Name }).ToListAsync(cancellationToken);
            // Our bill references, and the supplier's own invoice numbers, which often look the same.
            var bills = await db.SupplierBills.AsNoTracking()
                .Where(b => numbers.Contains(b.Number) || numbers.Contains(b.SupplierInvoiceNumber))
                .Select(b => new { b.Number, b.SupplierInvoiceNumber, b.SupplierId, b.SupplierName }).ToListAsync(cancellationToken);
            var returns = await db.SupplierReturns.AsNoTracking()
                .Where(r => numbers.Contains(r.Number) || (r.SupplierCreditNoteNumber != null && numbers.Contains(r.SupplierCreditNoteNumber)))
                .Select(r => new { r.Number, r.SupplierCreditNoteNumber, r.SupplierId, r.SupplierName }).ToListAsync(cancellationToken);

            foreach (var number in numbers)
            {
                var owner = orders.Where(o => o.Number == number).Select(o => (o.SupplierId, o.SupplierName))
                    .Concat(bills.Where(b => b.Number == number || b.SupplierInvoiceNumber == number).Select(b => (b.SupplierId, b.SupplierName)))
                    .Concat(returns.Where(r => r.Number == number || r.SupplierCreditNoteNumber == number).Select(r => (r.SupplierId, r.SupplierName)))
                    .Cast<(int SupplierId, string SupplierName)?>()
                    .FirstOrDefault();
                if (owner is null)
                {
                    warnings.Add(L.Msg($"{number} isn't a purchase order, bill or supplier return in SalesApp."));
                }
                else if (owner.Value.SupplierId != supplier.Id)
                {
                    warnings.Add(L.Msg($"{number} belongs to {owner.Value.SupplierName}, not {supplier.Name}."));
                }
            }
        }

        return (supplier.Name, warnings, null);
    }

    private sealed record Recipient(int Id, string Name, string? ContactPerson, string? Email, string? Phone);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ToolOutcome Error(string message) => new(message, true, "Couldn't write the draft");
}
