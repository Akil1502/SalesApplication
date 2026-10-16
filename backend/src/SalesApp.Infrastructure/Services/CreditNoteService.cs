using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Returns;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Returns;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ICreditNoteService
{
    Task<PagedResult<CreditNoteListItemDto>> ListAsync(CreditNoteListRequest request, CancellationToken cancellationToken);
    Task<CreditNoteSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AvailableCreditDto>> AvailableAsync(int customerId, CancellationToken cancellationToken);
    Task<CreditNoteDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<CreditNotePreviewDto> PreviewAsync(CreditNoteCreateRequest request, CancellationToken cancellationToken);
    Task<CreditNoteDto> CreateAsync(CreditNoteCreateRequest request, CancellationToken cancellationToken);
    Task<CreditNoteDto> ApplyAsync(int id, CreditApplyRequest request, CancellationToken cancellationToken);
    Task<CreditNoteDto> RefundAsync(int id, RefundCreateRequest request, CancellationToken cancellationToken);
    Task<CreditNoteDto> CancelAsync(int id, ReturnCancelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Credit notes against invoices. Issuing one is a single transaction: lock the invoice, work out each line from what
/// earlier credit notes left, allocate the next CN number, put returned goods back into stock (or write damaged ones
/// off), and take the value off what's still due on the invoice. Whatever is left is credit on the customer's account.
/// </summary>
public sealed class CreditNoteService(
    AppDbContext db,
    IDocumentNumbers numbers,
    IStockLedger ledger,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICreditNoteService
{
    private const string Prefix = "CN";

    public async Task<PagedResult<CreditNoteListItemDto>> ListAsync(CreditNoteListRequest request, CancellationToken cancellationToken)
    {
        var query = db.CreditNotes.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(c => c.Kind == kind);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(c => c.CustomerId == customerId);
        }

        if (request.InvoiceId is { } invoiceId)
        {
            query = query.Where(c => c.InvoiceId == invoiceId);
        }

        if (request.From is { } from)
        {
            query = query.Where(c => c.CreditNoteDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(c => c.CreditNoteDate <= to);
        }

        if (request.WithCreditOnly)
        {
            query = query.Where(c => c.Status == ReturnDocumentStatus.Issued && c.GrandTotal - c.AmountAdjusted - c.AmountRefunded > 0);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(c => c.Number.Contains(q) || c.CustomerName.Contains(q) || c.Invoice!.Number.Contains(q) ||
                                     (c.CustomerGstin != null && c.CustomerGstin.Contains(q)));
        }

        var page = await query
            .OrderByDescending(c => c.CreditNoteDate)
            .ThenByDescending(c => c.Id)
            .Select(c => new CreditNoteListItemDto(
                c.Id, c.Number, c.CreditNoteDate, c.Kind, c.Reason, c.Status, c.WithGst, c.InvoiceId, c.Invoice!.Number,
                c.CustomerId, c.CustomerName, c.TaxableValue, c.GrandTotal,
                c.Status == ReturnDocumentStatus.Issued ? c.GrandTotal - c.AmountAdjusted - c.AmountRefunded : 0m))
            .ToPagedResultAsync(request, cancellationToken);
        return page;
    }

    public async Task<CreditNoteSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var issued = db.CreditNotes.AsNoTracking().Where(c => c.Status == ReturnDocumentStatus.Issued);

        var thisMonth = issued.Where(c => c.CreditNoteDate >= monthStart && c.CreditNoteDate <= today);
        var credited = await thisMonth.SumAsync(c => (decimal?)c.GrandTotal, cancellationToken) ?? 0m;
        var count = await thisMonth.CountAsync(cancellationToken);
        var withCredit = issued.Where(c => c.GrandTotal - c.AmountAdjusted - c.AmountRefunded > 0);
        var available = await withCredit.SumAsync(c => (decimal?)(c.GrandTotal - c.AmountAdjusted - c.AmountRefunded), cancellationToken) ?? 0m;
        var customers = await withCredit.Select(c => c.CustomerId).Distinct().CountAsync(cancellationToken);
        var refunded = await db.CreditRefunds.Where(r => r.RefundDate >= monthStart && r.RefundDate <= today)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

        return new CreditNoteSummaryDto(credited, count, available, customers, refunded);
    }

    public async Task<IReadOnlyList<AvailableCreditDto>> AvailableAsync(int customerId, CancellationToken cancellationToken) =>
        await db.CreditNotes.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.Status == ReturnDocumentStatus.Issued && c.GrandTotal - c.AmountAdjusted - c.AmountRefunded > 0)
            .OrderBy(c => c.CreditNoteDate).ThenBy(c => c.Id)
            .Select(c => new AvailableCreditDto(c.Id, c.Number, c.CreditNoteDate, c.GrandTotal, c.GrandTotal - c.AmountAdjusted - c.AmountRefunded))
            .ToListAsync(cancellationToken);

    public async Task<CreditNoteDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var note = await db.CreditNotes
            .AsNoTracking()
            .Include(c => c.Lines)
            .Include(c => c.Invoice)
            .Include(c => c.CreatedBy)
            .Include(c => c.Adjustments).ThenInclude(a => a.Invoice)
            .Include(c => c.Adjustments).ThenInclude(a => a.CreatedBy)
            .Include(c => c.Refunds).ThenInclude(r => r.CreatedBy)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Credit note {id} was not found.");

        var lines = note.Lines.OrderBy(l => l.LineNumber).ToList();
        var hsnSummary = lines
            .GroupBy(l => new { l.HsnCode, l.GstRate })
            .OrderBy(g => g.Key.HsnCode).ThenBy(g => g.Key.GstRate)
            .Select(g => new HsnSummaryDto(g.Key.HsnCode, note.WithGst ? g.Key.GstRate : 0m, g.Sum(l => l.TaxableValue),
                g.Sum(l => l.CgstAmount), g.Sum(l => l.SgstAmount), g.Sum(l => l.IgstAmount)))
            .ToList();

        var cancelled = note.Status == ReturnDocumentStatus.Cancelled;
        var adjustments = note.Adjustments.OrderBy(a => a.AdjustmentDate).ThenBy(a => a.Id)
            .Select(a => new CreditUseDto(a.Id, note.Id, note.Number, a.InvoiceId, a.Invoice!.Number, a.AdjustmentDate, a.Amount, cancelled,
                a.CreatedBy!.FullName, a.CreatedAtUtc))
            .ToList();
        var refunds = note.Refunds.OrderBy(r => r.RefundDate).ThenBy(r => r.Id)
            .Select(r => new RefundDto(r.Id, r.RefundDate, r.Amount, r.Mode, r.Reference, r.Notes, r.CreatedBy!.FullName, r.CreatedAtUtc))
            .ToList();
        var a = note.BillingAddress;

        return new CreditNoteDto(
            note.Id, note.Number, FinancialYear.Label(note.CreditNoteDate), note.CreditNoteDate, note.Status, note.Kind, note.Reason, note.ReasonNote,
            note.WithGst, note.InvoiceId, note.Invoice!.Number, note.Invoice.InvoiceDate,
            note.SellerName, note.SellerGstin, note.SellerStateCode, note.SellerAddress,
            note.CustomerId, note.CustomerName, note.CustomerGstin,
            new InvoiceAddressDto(a.Line1, a.Line2, a.City, a.StateCode, a.StateName, a.Pincode),
            note.PlaceOfSupplyStateCode, note.PlaceOfSupplyStateName, note.IsInterState, note.UsesUtgst,
            [.. lines.Select(l => new CreditNoteLineDto(l.LineNumber, l.InvoiceLineId, l.ProductId, l.ProductSku, l.ProductName, l.HsnCode, l.Unit,
                l.Quantity, l.Disposition, l.TaxableValue, l.GstRate, l.CgstAmount, l.SgstAmount, l.IgstAmount, l.LineTotal))],
            hsnSummary,
            note.TaxableValue, note.CgstAmount, note.SgstAmount, note.IgstAmount, note.RoundOff, note.GrandTotal,
            note.AmountAdjusted, note.AmountRefunded, cancelled ? 0m : note.GrandTotal - note.AmountAdjusted - note.AmountRefunded,
            AmountInWords.Rupees(note.GrandTotal),
            adjustments, refunds, note.CancelledAtUtc, note.CancellationReason,
            CancelBlockedReason(note),
            note.CreatedBy!.FullName, note.CreatedAtUtc, RowVersions.ToToken(note.RowVersion));
    }

    public async Task<CreditNotePreviewDto> PreviewAsync(CreditNoteCreateRequest request, CancellationToken cancellationToken)
    {
        var draft = await BuildAsync(request, cancellationToken);
        return new CreditNotePreviewDto(
            [.. draft.Lines.Select(l => new CreditNotePreviewLineDto(l.Line.Id, l.Amounts.TaxableValue,
                l.Amounts.CgstAmount + l.Amounts.SgstAmount + l.Amounts.IgstAmount, l.Amounts.LineTotal))],
            draft.WithGst, ReturnRules.GstDeadline(draft.Invoice.InvoiceDate),
            draft.Lines.Sum(l => l.Amounts.TaxableValue), draft.Lines.Sum(l => l.Amounts.CgstAmount), draft.Lines.Sum(l => l.Amounts.SgstAmount),
            draft.Lines.Sum(l => l.Amounts.IgstAmount), draft.RoundOff, draft.GrandTotal, draft.OffInvoice, draft.GrandTotal - draft.OffInvoice);
    }

    public async Task<CreditNoteDto> CreateAsync(CreditNoteCreateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Credits.LockInvoiceAsync(db, request.InvoiceId, cancellationToken);

        var draft = await BuildAsync(request, cancellationToken);
        var invoice = draft.Invoice;

        // Allocating the number takes the financial year's lock, so the date check can't race another credit note.
        var number = await numbers.NextAsync(DocumentTypes.CreditNote, Prefix, draft.Date, cancellationToken);
        var financialYear = FinancialYear.StartYear(draft.Date);
        var latest = await db.CreditNotes.Where(c => c.FinancialYear == financialYear).MaxAsync(c => (DateOnly?)c.CreditNoteDate, cancellationToken);
        if (latest is { } latestDate && draft.Date < latestDate)
        {
            throw new ValidationException("creditNoteDate",
                $"Credit notes are numbered in date order. The latest one this financial year is dated {latestDate:dd MMM yyyy}, so this one can't be earlier.");
        }

        var note = new CreditNote
        {
            Number = number,
            FinancialYear = financialYear,
            CreditNoteDate = draft.Date,
            Kind = request.Kind,
            Reason = request.Reason,
            ReasonNote = Text.NullIfBlank(request.ReasonNote),
            WithGst = draft.WithGst,
            InvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            SellerName = invoice.SellerName,
            SellerGstin = invoice.SellerGstin,
            SellerStateCode = invoice.SellerStateCode,
            SellerAddress = invoice.SellerAddress,
            CustomerName = invoice.CustomerName,
            CustomerGstin = invoice.CustomerGstin,
            BillingAddress = new InvoiceAddress
            {
                Line1 = invoice.BillingAddress.Line1,
                Line2 = invoice.BillingAddress.Line2,
                City = invoice.BillingAddress.City,
                StateCode = invoice.BillingAddress.StateCode,
                StateName = invoice.BillingAddress.StateName,
                Pincode = invoice.BillingAddress.Pincode,
            },
            PlaceOfSupplyStateCode = invoice.PlaceOfSupplyStateCode,
            PlaceOfSupplyStateName = invoice.PlaceOfSupplyStateName,
            IsInterState = invoice.IsInterState,
            UsesUtgst = invoice.UsesUtgst,
            TaxableValue = draft.Lines.Sum(l => l.Amounts.TaxableValue),
            CgstAmount = draft.Lines.Sum(l => l.Amounts.CgstAmount),
            SgstAmount = draft.Lines.Sum(l => l.Amounts.SgstAmount),
            IgstAmount = draft.Lines.Sum(l => l.Amounts.IgstAmount),
            RoundOff = draft.RoundOff,
            GrandTotal = draft.GrandTotal,
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        };

        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var (line, input, amounts) = draft.Lines[i];
            note.Lines.Add(new CreditNoteLine
            {
                LineNumber = i + 1,
                InvoiceLineId = line.Id,
                ProductId = line.ProductId,
                ProductSku = line.ProductSku,
                ProductName = line.ProductName,
                HsnCode = line.HsnCode,
                Unit = line.Unit,
                Quantity = request.Kind == CreditNoteKind.GoodsReturn ? input.Quantity : null,
                Disposition = request.Kind == CreditNoteKind.GoodsReturn ? input.Disposition : null,
                TaxableValue = amounts.TaxableValue,
                GstRate = draft.WithGst ? line.GstRate : 0m,
                CgstAmount = amounts.CgstAmount,
                SgstAmount = amounts.SgstAmount,
                IgstAmount = amounts.IgstAmount,
                LineTotal = amounts.LineTotal,
            });
        }

        db.CreditNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        if (request.Kind == CreditNoteKind.GoodsReturn)
        {
            await PostReturnedGoodsAsync(note, cancellationToken);
        }

        // The credit first comes off what's still due on its own invoice.
        if (draft.OffInvoice > 0)
        {
            if (!await Credits.CreditInvoiceAsync(db, invoice.Id, draft.OffInvoice, cancellationToken))
            {
                throw new ConflictException($"{invoice.Number} changed while the credit note was being issued (a payment came in). Try again.");
            }

            note.Adjustments.Add(new CreditAdjustment
            {
                InvoiceId = invoice.Id,
                AdjustmentDate = draft.Date,
                Amount = draft.OffInvoice,
                CreatedByUserId = currentUser.UserId,
                CreatedAtUtc = Now,
            });
            note.AmountAdjusted = draft.OffInvoice;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(note.Id, cancellationToken);
    }

    public async Task<CreditNoteDto> ApplyAsync(int id, CreditApplyRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var note = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        var available = Available(note);
        if (request.Amount > available)
        {
            throw new ValidationException("amount", available > 0
                ? $"Only {Credits.Rupees(available)} of credit is left on {note.Number}."
                : $"No credit is left on {note.Number}.");
        }

        var invoice = await db.Invoices.AsNoTracking()
            .Where(i => i.Id == request.DocumentId)
            .Select(i => new { i.Id, i.Number, i.CustomerId, i.Status, i.InvoiceDate, Balance = i.GrandTotal - i.AmountPaid - i.AmountCredited })
            .SingleOrDefaultAsync(cancellationToken);
        if (invoice is null || invoice.CustomerId != note.CustomerId)
        {
            throw new ValidationException("documentId", $"Choose one of {note.CustomerName}'s invoices.");
        }

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            throw new ValidationException("documentId", $"{invoice.Number} is cancelled.");
        }

        if (request.Amount > invoice.Balance)
        {
            throw new ValidationException("amount", invoice.Balance > 0
                ? $"Only {Credits.Rupees(invoice.Balance)} is due on {invoice.Number}."
                : $"Nothing is due on {invoice.Number}.");
        }

        var date = ValidateDate(request.Date, "date", Later(note.CreditNoteDate, invoice.InvoiceDate), "the credit note or the invoice");
        if (!await Credits.CreditInvoiceAsync(db, invoice.Id, request.Amount, cancellationToken))
        {
            throw new ConflictException($"{invoice.Number} changed meanwhile (another payment or credit). Reload and try again.");
        }

        note.Adjustments.Add(new CreditAdjustment
        {
            InvoiceId = invoice.Id,
            AdjustmentDate = date,
            Amount = request.Amount,
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        });
        note.AmountAdjusted += request.Amount;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CreditNoteDto> RefundAsync(int id, RefundCreateRequest request, CancellationToken cancellationToken)
    {
        var note = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        var available = Available(note);
        if (request.Amount > available)
        {
            throw new ValidationException("amount", available > 0
                ? $"Only {Credits.Rupees(available)} of credit is left on {note.Number} to refund."
                : $"No credit is left on {note.Number} to refund.");
        }

        var date = ValidateDate(request.RefundDate, "refundDate", note.CreditNoteDate, "the credit note");
        note.Refunds.Add(new CreditRefund
        {
            RefundDate = date,
            Amount = request.Amount,
            Mode = request.Mode,
            Reference = Text.NullIfBlank(request.Reference),
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        });
        note.AmountRefunded += request.Amount;

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CreditNoteDto> CancelAsync(int id, ReturnCancelRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var note = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        await db.Entry(note).Collection(c => c.Lines).LoadAsync(cancellationToken);
        await db.Entry(note).Collection(c => c.Adjustments).Query().Include(a => a.Invoice).LoadAsync(cancellationToken);

        if (CancelBlockedReason(note) is { } blocked)
        {
            throw new ConflictException(blocked);
        }

        // Only its own invoice can hold its credit here; take that back off.
        var ownCredit = note.Adjustments.Sum(a => a.Amount);
        if (ownCredit > 0 && !await Credits.CreditInvoiceAsync(db, note.InvoiceId, -ownCredit, cancellationToken))
        {
            throw new ConflictException("The invoice changed meanwhile. Reload and try again.");
        }

        note.Status = ReturnDocumentStatus.Cancelled;
        note.CancelledAtUtc = Now;
        note.CancellationReason = request.Reason.Trim();

        // Goods that went back on the shelf leave it again. Damaged ones were written off on the day, so nothing moves.
        foreach (var line in note.Lines.Where(l => l.Disposition == ReturnDisposition.Restock && l.Quantity is > 0))
        {
            await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.SaleReturn, -line.Quantity!.Value, clock.Today,
                ReferenceType: StockReferenceTypes.CreditNote, ReferenceId: note.Id, ReferenceNumber: note.Number,
                Notes: $"Credit note cancelled: {note.CancellationReason}"), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private sealed record DraftLine(InvoiceLine Line, CreditNoteLineInput Input, CreditAmounts Amounts);

    private sealed record Draft(Invoice Invoice, DateOnly Date, bool WithGst, List<DraftLine> Lines, decimal RoundOff, decimal GrandTotal, decimal OffInvoice);

    /// <summary>Works the credit note out from the invoice and what earlier credit notes left. Saves nothing.</summary>
    private async Task<Draft> BuildAsync(CreditNoteCreateRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new ValidationException("invoiceId", "Choose a valid invoice.");
        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            throw new ConflictException($"{invoice.Number} is cancelled, so there's nothing to credit.");
        }

        var date = ValidateDate(request.CreditNoteDate, "creditNoteDate", invoice.InvoiceDate, "the invoice");
        var withGst = ReturnRules.CarriesGst(invoice.InvoiceDate, date);
        var soFar = await Credits.CreditedSoFarAsync(db, invoice.Id, cancellationToken);
        var goods = request.Kind == CreditNoteKind.GoodsReturn;

        var errors = new Dictionary<string, string[]>();
        var lines = new List<DraftLine>();
        var seen = new HashSet<int>();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var input = request.Lines[i];
            var key = $"lines[{i}]";
            var line = invoice.Lines.FirstOrDefault(l => l.Id == input.InvoiceLineId);
            if (line is null)
            {
                errors[$"{key}.invoiceLineId"] = [$"That line isn't on {invoice.Number}."];
                continue;
            }

            if (!seen.Add(line.Id))
            {
                errors[$"{key}.invoiceLineId"] = ["This line is listed twice. Combine the quantities."];
                continue;
            }

            var creditable = new CreditableLine(line.Quantity, line.TaxableValue, line.CgstAmount, line.SgstAmount, line.IgstAmount, line.GstRate);
            var credited = soFar.GetValueOrDefault(line.Id, CreditedSoFar.None);
            var valueLeft = ReturnRules.RemainingValue(creditable, credited);
            if (valueLeft <= 0)
            {
                errors[$"{key}.{(goods ? "quantity" : "amount")}"] = ["This line has been fully credited already."];
                continue;
            }

            if (goods)
            {
                var quantityLeft = ReturnRules.RemainingQuantity(creditable, credited);
                if (input.Quantity is not { } quantity)
                {
                    errors[$"{key}.quantity"] = ["Enter the quantity returned."];
                }
                else if (quantity > quantityLeft)
                {
                    errors[$"{key}.quantity"] = [quantityLeft > 0
                        ? $"Only {Qty(quantityLeft)} {line.Unit} can still be returned: {Qty(line.Quantity)} invoiced, {Qty(credited.Quantity)} returned already."
                        : "Everything on this line has been returned already."];
                }
                else if (input.Disposition is null)
                {
                    errors[$"{key}.disposition"] = ["Say whether the goods go back into stock or were damaged."];
                }
                else
                {
                    lines.Add(new DraftLine(line, input, ReturnRules.ForGoods(creditable, credited, quantity, invoice.IsInterState, withGst)));
                }
            }
            else if (input.Amount is not { } amount)
            {
                errors[$"{key}.amount"] = ["Enter the amount to credit."];
            }
            else if (amount > valueLeft)
            {
                errors[$"{key}.amount"] = [$"At most {Credits.Rupees(valueLeft)} (before GST) can still be credited on this line."];
            }
            else
            {
                lines.Add(new DraftLine(line, input, ReturnRules.ForValue(creditable, credited, amount, invoice.IsInterState, withGst)));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }

        var (roundOff, grandTotal) = ReturnRules.Total(lines.Select(l => l.Amounts));
        if (grandTotal <= 0)
        {
            throw new ValidationException("lines", "The credit note comes to less than ₹1.");
        }

        var balance = invoice.GrandTotal - invoice.AmountPaid - invoice.AmountCredited;
        return new Draft(invoice, date, withGst, lines, roundOff, grandTotal, Math.Clamp(balance, 0m, grandTotal));
    }

    /// <summary>
    /// Returned goods come back into stock at the product's current cost. Damaged ones are recorded as returned and
    /// written off straight away, so the ledger shows both the return and the loss.
    /// </summary>
    private async Task PostReturnedGoodsAsync(CreditNote note, CancellationToken cancellationToken)
    {
        var productIds = note.Lines.Select(l => l.ProductId).Distinct().ToList();
        var costs = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.CostPrice, cancellationToken);
        var invoiceNumber = await db.Invoices.Where(i => i.Id == note.InvoiceId).Select(i => i.Number).SingleAsync(cancellationToken);

        foreach (var line in note.Lines)
        {
            var quantity = line.Quantity!.Value;
            await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.SaleReturn, quantity, note.CreditNoteDate,
                UnitCost: costs.GetValueOrDefault(line.ProductId),
                ReferenceType: StockReferenceTypes.CreditNote, ReferenceId: note.Id, ReferenceNumber: note.Number,
                Notes: $"Returned against {invoiceNumber}"), cancellationToken);

            if (line.Disposition == ReturnDisposition.Damaged)
            {
                await ledger.PostAsync(new StockPosting(
                    line.ProductId, StockMovementType.Adjustment, -quantity, note.CreditNoteDate,
                    Reason: AdjustmentReason.Damaged,
                    ReferenceType: StockReferenceTypes.CreditNote, ReferenceId: note.Id, ReferenceNumber: note.Number,
                    Notes: "Came back damaged: written off"), cancellationToken);
            }
        }
    }

    private async Task<CreditNote> LoadForChangeAsync(int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var note = await db.CreditNotes.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Credit note {id} was not found.");
        db.Entry(note).Property(c => c.RowVersion).OriginalValue = RowVersions.Parse(rowVersion);
        if (note.Status == ReturnDocumentStatus.Cancelled)
        {
            throw new ConflictException($"{note.Number} is cancelled.");
        }

        return note;
    }

    /// <summary>Needs the note's adjustments (with their invoices) loaded.</summary>
    private string? CancelBlockedReason(CreditNote note)
    {
        var elsewhere = note.Adjustments.Where(a => a.InvoiceId != note.InvoiceId).Select(a => a.Invoice?.Number).Distinct().ToList();
        return Credits.ReturnCancelBlockedReason(note.Number, note.Status, note.CreditNoteDate, note.AmountRefunded,
            elsewhere.Count == 0 ? null : string.Join(", ", elsewhere), clock.Today);
    }

    private DateOnly ValidateDate(DateOnly? requested, string field, DateOnly earliest, string earliestName)
    {
        var today = clock.Today;
        var date = requested ?? today;
        if (date > today)
        {
            throw new ValidationException(field, "The date can't be in the future.");
        }

        return date < earliest ? throw new ValidationException(field, $"The date can't be before {earliestName} ({earliest:dd MMM yyyy}).") : date;
    }

    private static DateOnly Later(DateOnly a, DateOnly b) => a > b ? a : b;

    private static decimal Available(CreditNote note) => note.GrandTotal - note.AmountAdjusted - note.AmountRefunded;

    private static string Qty(decimal value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}
