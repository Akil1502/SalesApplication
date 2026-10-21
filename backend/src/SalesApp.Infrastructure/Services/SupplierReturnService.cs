using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Returns;
using SalesApp.Core.Entities;
using SalesApp.Core.Returns;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ISupplierReturnService
{
    Task<PagedResult<SupplierReturnListItemDto>> ListAsync(SupplierReturnListRequest request, CancellationToken cancellationToken);
    Task<SupplierReturnSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AvailableCreditDto>> AvailableAsync(int supplierId, CancellationToken cancellationToken);
    Task<SupplierReturnDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<SupplierReturnPreviewDto> PreviewAsync(SupplierReturnCreateRequest request, CancellationToken cancellationToken);
    Task<SupplierReturnDto> CreateAsync(SupplierReturnCreateRequest request, CancellationToken cancellationToken);
    Task<SupplierReturnDto> NoteSupplierCreditNoteAsync(int id, SupplierReturnNoteRequest request, CancellationToken cancellationToken);
    Task<SupplierReturnDto> ApplyAsync(int id, CreditApplyRequest request, CancellationToken cancellationToken);
    Task<SupplierReturnDto> RefundAsync(int id, RefundCreateRequest request, CancellationToken cancellationToken);
    Task<SupplierReturnDto> CancelAsync(int id, ReturnCancelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Goods sent back to suppliers against their bills (our debit notes). Issuing one is a single transaction: lock the
/// bill, take the goods out of stock at the bill's price (re-averaging the cost of what stays), reverse the input GST,
/// and take the value off what we still owe on the bill. Whatever is left is credit with the supplier.
/// </summary>
public sealed class SupplierReturnService(
    AppDbContext db,
    IDocumentNumbers numbers,
    IStockLedger ledger,
    ILookupService lookups,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ISupplierReturnService
{
    private const string Prefix = "DN";

    public async Task<PagedResult<SupplierReturnListItemDto>> ListAsync(SupplierReturnListRequest request, CancellationToken cancellationToken)
    {
        var query = db.SupplierReturns.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(r => r.SupplierId == supplierId);
        }

        if (request.SupplierBillId is { } billId)
        {
            query = query.Where(r => r.SupplierBillId == billId);
        }

        if (request.From is { } from)
        {
            query = query.Where(r => r.ReturnDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(r => r.ReturnDate <= to);
        }

        if (request.WithCreditOnly)
        {
            query = query.Where(r => r.Status == ReturnDocumentStatus.Issued && r.GrandTotal - r.AmountAdjusted - r.AmountRefunded > 0);
        }

        if (request.AwaitingSupplierNoteOnly)
        {
            query = query.Where(r => r.Status == ReturnDocumentStatus.Issued && r.SupplierCreditNoteNumber == null);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(r => r.Number.Contains(q) || r.SupplierName.Contains(q) || r.SupplierBill!.Number.Contains(q) ||
                                     r.SupplierBill.SupplierInvoiceNumber.Contains(q) ||
                                     (r.SupplierCreditNoteNumber != null && r.SupplierCreditNoteNumber.Contains(q)));
        }

        return await query
            .OrderByDescending(r => r.ReturnDate)
            .ThenByDescending(r => r.Id)
            .Select(r => new SupplierReturnListItemDto(
                r.Id, r.Number, r.ReturnDate, r.Reason, r.Status, r.WithGst, r.SupplierBillId, r.SupplierBill!.Number,
                r.SupplierBill.SupplierInvoiceNumber, r.SupplierId, r.SupplierName, r.SupplierCreditNoteNumber, r.TaxableValue, r.GrandTotal,
                r.Status == ReturnDocumentStatus.Issued ? r.GrandTotal - r.AmountAdjusted - r.AmountRefunded : 0m))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<SupplierReturnSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var issued = db.SupplierReturns.AsNoTracking().Where(r => r.Status == ReturnDocumentStatus.Issued);

        var thisMonth = issued.Where(r => r.ReturnDate >= monthStart && r.ReturnDate <= today);
        var returned = await thisMonth.SumAsync(r => (decimal?)r.GrandTotal, cancellationToken) ?? 0m;
        var count = await thisMonth.CountAsync(cancellationToken);
        var available = await issued.SumAsync(r => (decimal?)(r.GrandTotal - r.AmountAdjusted - r.AmountRefunded), cancellationToken) ?? 0m;
        var awaiting = await issued.CountAsync(r => r.SupplierCreditNoteNumber == null, cancellationToken);
        return new SupplierReturnSummaryDto(returned, count, available, awaiting);
    }

    public async Task<IReadOnlyList<AvailableCreditDto>> AvailableAsync(int supplierId, CancellationToken cancellationToken) =>
        [.. (await db.SupplierReturns.AsNoTracking()
                .Where(r => r.SupplierId == supplierId && r.Status == ReturnDocumentStatus.Issued && r.GrandTotal - r.AmountAdjusted - r.AmountRefunded > 0)
                .OrderBy(r => r.ReturnDate).ThenBy(r => r.Id)
                .Select(r => new { r.Id, r.Number, r.ReturnDate, r.GrandTotal, Available = r.GrandTotal - r.AmountAdjusted - r.AmountRefunded, r.RowVersion })
                .ToListAsync(cancellationToken))
            .Select(r => new AvailableCreditDto(r.Id, r.Number, r.ReturnDate, r.GrandTotal, r.Available, RowVersions.ToToken(r.RowVersion)))];

    public async Task<SupplierReturnDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var ret = await db.SupplierReturns
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.SupplierBill).ThenInclude(b => b!.PurchaseOrder)
            .Include(r => r.CreatedBy)
            .Include(r => r.Adjustments).ThenInclude(a => a.SupplierBill)
            .Include(r => r.Adjustments).ThenInclude(a => a.CreatedBy)
            .Include(r => r.Refunds).ThenInclude(f => f.CreatedBy)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier return {id} was not found.");

        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
        var states = await lookups.GetStatesAsync(cancellationToken);
        var bill = ret.SupplierBill!;
        var cancelled = ret.Status == ReturnDocumentStatus.Cancelled;

        return new SupplierReturnDto(
            ret.Id, ret.Number, FinancialYear.Label(ret.ReturnDate), ret.ReturnDate, ret.Status, ret.Reason, ret.ReasonNote, ret.WithGst,
            bill.Id, bill.Number, bill.SupplierInvoiceNumber, bill.BillDate, bill.PurchaseOrderId, bill.PurchaseOrder!.Number,
            ret.SupplierId, ret.SupplierName, ret.SupplierGstin, ret.SupplierStateCode,
            states.GetValueOrDefault(ret.SupplierStateCode)?.Name ?? ret.SupplierStateCode, ret.IsInterState,
            settings.LegalName, settings.Gstin, InvoiceService.SellerAddress(settings, states),
            ret.SupplierCreditNoteNumber, ret.SupplierCreditNoteDate,
            [.. ret.Lines.OrderBy(l => l.LineNumber).Select(l => new SupplierReturnLineDto(l.LineNumber, l.SupplierBillLineId, l.ProductId,
                l.ProductSku, l.ProductName, l.HsnCode, l.Unit, l.Quantity, l.UnitPrice, l.TaxableValue, l.GstRate,
                l.CgstAmount, l.SgstAmount, l.IgstAmount, l.LineTotal))],
            ret.TaxableValue, ret.CgstAmount, ret.SgstAmount, ret.IgstAmount, ret.RoundOff, ret.GrandTotal,
            ret.AmountAdjusted, ret.AmountRefunded, cancelled ? 0m : ret.GrandTotal - ret.AmountAdjusted - ret.AmountRefunded,
            AmountInWords.Rupees(ret.GrandTotal),
            AmountInWords.TamilRupees(ret.GrandTotal),
            await DocumentLanguages.ForSupplierAsync(db, ret.SupplierId, cancellationToken),
            [.. ret.Adjustments.OrderBy(a => a.AdjustmentDate).ThenBy(a => a.Id).Select(a => new CreditUseDto(a.Id, ret.Id, ret.Number,
                a.SupplierBillId, a.SupplierBill!.Number, a.AdjustmentDate, a.Amount, cancelled, a.CreatedBy!.FullName, a.CreatedAtUtc))],
            [.. ret.Refunds.OrderBy(f => f.RefundDate).ThenBy(f => f.Id).Select(f => new RefundDto(f.Id, f.RefundDate, f.Amount, f.Mode,
                f.Reference, f.Notes, f.CreatedBy!.FullName, f.CreatedAtUtc))],
            ret.CancelledAtUtc, ret.CancellationReason, L.T(CancelBlockedReason(ret)),
            ret.CreatedBy!.FullName, ret.CreatedAtUtc, RowVersions.ToToken(ret.RowVersion));
    }

    public async Task<SupplierReturnPreviewDto> PreviewAsync(SupplierReturnCreateRequest request, CancellationToken cancellationToken)
    {
        var draft = await BuildAsync(request, cancellationToken);
        return new SupplierReturnPreviewDto(
            [.. draft.Lines.Select(l => new SupplierReturnPreviewLineDto(l.Line.Id, l.Amounts.TaxableValue,
                l.Amounts.CgstAmount + l.Amounts.SgstAmount + l.Amounts.IgstAmount, l.Amounts.LineTotal))],
            draft.WithGst, ReturnRules.GstDeadline(draft.Bill.BillDate),
            draft.Lines.Sum(l => l.Amounts.TaxableValue), draft.Lines.Sum(l => l.Amounts.CgstAmount), draft.Lines.Sum(l => l.Amounts.SgstAmount),
            draft.Lines.Sum(l => l.Amounts.IgstAmount), draft.RoundOff, draft.GrandTotal, draft.OffBill, draft.GrandTotal - draft.OffBill);
    }

    public async Task<SupplierReturnDto> CreateAsync(SupplierReturnCreateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Credits.LockBillAsync(db, request.SupplierBillId, cancellationToken);

        var draft = await BuildAsync(request, cancellationToken);
        var bill = draft.Bill;

        var number = await numbers.NextAsync(DocumentTypes.DebitNote, Prefix, draft.Date, cancellationToken);
        var financialYear = FinancialYear.StartYear(draft.Date);
        var latest = await db.SupplierReturns.Where(r => r.FinancialYear == financialYear).MaxAsync(r => (DateOnly?)r.ReturnDate, cancellationToken);
        if (latest is { } latestDate && draft.Date < latestDate)
        {
            throw new ValidationException("returnDate",
                $"Debit notes are numbered in date order. The latest one this financial year is dated {latestDate:dd MMM yyyy}, so this one can't be earlier.");
        }

        var ret = new SupplierReturn
        {
            Number = number,
            FinancialYear = financialYear,
            ReturnDate = draft.Date,
            Reason = request.Reason,
            ReasonNote = Text.NullIfBlank(request.ReasonNote),
            WithGst = draft.WithGst,
            SupplierBillId = bill.Id,
            SupplierId = bill.SupplierId,
            SupplierName = bill.SupplierName,
            SupplierGstin = bill.SupplierGstin,
            SupplierStateCode = bill.SupplierStateCode,
            IsInterState = bill.IsInterState,
            SupplierCreditNoteNumber = Text.NullIfBlank(request.SupplierCreditNoteNumber),
            SupplierCreditNoteDate = Text.NullIfBlank(request.SupplierCreditNoteNumber) is null ? null : request.SupplierCreditNoteDate,
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
            var (line, quantity, amounts) = draft.Lines[i];
            ret.Lines.Add(new SupplierReturnLine
            {
                LineNumber = i + 1,
                SupplierBillLineId = line.Id,
                ProductId = line.ProductId,
                ProductSku = line.ProductSku,
                ProductName = line.ProductName,
                HsnCode = line.HsnCode,
                Unit = line.Unit,
                Quantity = quantity,
                UnitPrice = line.UnitPrice,
                TaxableValue = amounts.TaxableValue,
                GstRate = draft.WithGst ? line.GstRate : 0m,
                CgstAmount = amounts.CgstAmount,
                SgstAmount = amounts.SgstAmount,
                IgstAmount = amounts.IgstAmount,
                LineTotal = amounts.LineTotal,
            });
        }

        db.SupplierReturns.Add(ret);
        await db.SaveChangesAsync(cancellationToken);

        // The goods leave stock at what they cost us, and what stays keeps its true average cost. Not enough stock aborts it all.
        foreach (var line in ret.Lines)
        {
            var movement = await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.PurchaseReturn, -line.Quantity, ret.ReturnDate,
                UnitCost: line.UnitPrice,
                ReferenceType: StockReferenceTypes.SupplierReturn, ReferenceId: ret.Id, ReferenceNumber: ret.Number,
                Notes: L.Msg($"Returned to {ret.SupplierName} against {bill.Number}")), cancellationToken);
            await StockCosting.ApplyReturnAsync(db, line.ProductId, movement.BalanceAfter + line.Quantity, line.Quantity, line.UnitPrice, cancellationToken);
        }

        if (draft.OffBill > 0)
        {
            if (!await Credits.CreditBillAsync(db, bill.Id, draft.OffBill, cancellationToken))
            {
                throw new ConflictException($"{bill.Number} changed while the return was being recorded (a payment went out). Try again.");
            }

            ret.Adjustments.Add(new SupplierCreditAdjustment
            {
                SupplierBillId = bill.Id,
                AdjustmentDate = draft.Date,
                Amount = draft.OffBill,
                CreatedByUserId = currentUser.UserId,
                CreatedAtUtc = Now,
            });
            ret.AmountAdjusted = draft.OffBill;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ret.Id, cancellationToken);
    }

    public async Task<SupplierReturnDto> NoteSupplierCreditNoteAsync(int id, SupplierReturnNoteRequest request, CancellationToken cancellationToken)
    {
        var ret = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (request.SupplierCreditNoteDate is { } date && date > clock.Today)
        {
            throw new ValidationException("supplierCreditNoteDate", "The date can't be in the future.");
        }

        ret.SupplierCreditNoteNumber = request.SupplierCreditNoteNumber.Trim();
        ret.SupplierCreditNoteDate = request.SupplierCreditNoteDate;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierReturnDto> ApplyAsync(int id, CreditApplyRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ret = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        var available = Available(ret);
        if (request.Amount > available)
        {
            throw new ValidationException("amount", available > 0
                ? $"Only {Credits.Rupees(available)} of credit is left on {ret.Number}."
                : $"No credit is left on {ret.Number}.");
        }

        var bill = await db.SupplierBills.AsNoTracking()
            .Where(b => b.Id == request.DocumentId)
            .Select(b => new { b.Id, b.Number, b.SupplierId, b.Status, b.BillDate, Balance = b.GrandTotal - b.AmountPaid - b.AmountCredited })
            .SingleOrDefaultAsync(cancellationToken);
        if (bill is null || bill.SupplierId != ret.SupplierId)
        {
            throw new ValidationException("documentId", $"Choose one of {ret.SupplierName}'s bills.");
        }

        if (bill.Status == SupplierBillStatus.Cancelled)
        {
            throw new ValidationException("documentId", $"{bill.Number} is cancelled.");
        }

        if (request.Amount > bill.Balance)
        {
            throw new ValidationException("amount", bill.Balance > 0
                ? $"Only {Credits.Rupees(bill.Balance)} is owed on {bill.Number}."
                : $"Nothing is owed on {bill.Number}.");
        }

        var date = ValidateDate(request.Date, "date", ret.ReturnDate > bill.BillDate ? ret.ReturnDate : bill.BillDate, L.Msg("the return or the bill"));
        if (!await Credits.CreditBillAsync(db, bill.Id, request.Amount, cancellationToken))
        {
            throw new ConflictException($"{bill.Number} changed meanwhile (another payment or credit). Reload and try again.");
        }

        ret.Adjustments.Add(new SupplierCreditAdjustment
        {
            SupplierBillId = bill.Id,
            AdjustmentDate = date,
            Amount = request.Amount,
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        });
        ret.AmountAdjusted += request.Amount;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierReturnDto> RefundAsync(int id, RefundCreateRequest request, CancellationToken cancellationToken)
    {
        var ret = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        var available = Available(ret);
        if (request.Amount > available)
        {
            throw new ValidationException("amount", available > 0
                ? $"Only {Credits.Rupees(available)} of credit is left on {ret.Number}."
                : $"No credit is left on {ret.Number}.");
        }

        var date = ValidateDate(request.RefundDate, "refundDate", ret.ReturnDate, L.Msg("the return"));
        ret.Refunds.Add(new SupplierRefund
        {
            RefundDate = date,
            Amount = request.Amount,
            Mode = request.Mode,
            Reference = Text.NullIfBlank(request.Reference),
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        });
        ret.AmountRefunded += request.Amount;

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierReturnDto> CancelAsync(int id, ReturnCancelRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ret = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        await db.Entry(ret).Collection(r => r.Lines).LoadAsync(cancellationToken);
        await db.Entry(ret).Collection(r => r.Adjustments).Query().Include(a => a.SupplierBill).LoadAsync(cancellationToken);

        if (CancelBlockedReason(ret) is { } blocked)
        {
            throw new ConflictException(blocked);
        }

        var ownCredit = ret.Adjustments.Sum(a => a.Amount);
        if (ownCredit > 0 && !await Credits.CreditBillAsync(db, ret.SupplierBillId, -ownCredit, cancellationToken))
        {
            throw new ConflictException("The bill changed meanwhile. Reload and try again.");
        }

        ret.Status = ReturnDocumentStatus.Cancelled;
        ret.CancelledAtUtc = Now;
        ret.CancellationReason = request.Reason.Trim();

        // The goods come back into stock at the price they went out at.
        foreach (var line in ret.Lines)
        {
            var movement = await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.PurchaseReturn, line.Quantity, clock.Today,
                UnitCost: line.UnitPrice,
                ReferenceType: StockReferenceTypes.SupplierReturn, ReferenceId: ret.Id, ReferenceNumber: ret.Number,
                Notes: L.Msg($"Return cancelled: {ret.CancellationReason}")), cancellationToken);
            await StockCosting.ApplyReceiptAsync(db, line.ProductId, movement.BalanceAfter - line.Quantity, line.Quantity, line.UnitPrice, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private sealed record DraftLine(SupplierBillLine Line, decimal Quantity, CreditAmounts Amounts);

    private sealed record Draft(SupplierBill Bill, DateOnly Date, bool WithGst, List<DraftLine> Lines, decimal RoundOff, decimal GrandTotal, decimal OffBill);

    private async Task<Draft> BuildAsync(SupplierReturnCreateRequest request, CancellationToken cancellationToken)
    {
        var bill = await db.SupplierBills.AsNoTracking().Include(b => b.Lines).SingleOrDefaultAsync(b => b.Id == request.SupplierBillId, cancellationToken)
            ?? throw new ValidationException("supplierBillId", "Choose a valid supplier bill.");
        if (bill.Status == SupplierBillStatus.Cancelled)
        {
            throw new ConflictException($"{bill.Number} is cancelled, so nothing can be returned against it.");
        }

        if (request.Reason is ReturnReason.RateDifference or ReturnReason.ShortSupply or ReturnReason.Discount)
        {
            throw new ValidationException("reason", "A supplier return is for goods sent back. Choose why they went back.");
        }

        var date = ValidateDate(request.ReturnDate, "returnDate", bill.BillDate, L.Msg("the bill"));
        if (request.SupplierCreditNoteDate is { } noteDate && noteDate > clock.Today)
        {
            throw new ValidationException("supplierCreditNoteDate", "The date can't be in the future.");
        }

        var withGst = ReturnRules.CarriesGst(bill.BillDate, date);
        var soFar = await Credits.ReturnedSoFarAsync(db, bill.Id, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var lines = new List<DraftLine>();
        var seen = new HashSet<int>();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var input = request.Lines[i];
            var key = $"lines[{i}]";
            var line = bill.Lines.FirstOrDefault(l => l.Id == input.SupplierBillLineId);
            if (line is null)
            {
                errors[$"{key}.supplierBillLineId"] = [$"That line isn't on {bill.Number}."];
                continue;
            }

            if (!seen.Add(line.Id))
            {
                errors[$"{key}.supplierBillLineId"] = ["This line is listed twice. Combine the quantities."];
                continue;
            }

            var creditable = new CreditableLine(line.Quantity, line.TaxableValue, line.CgstAmount, line.SgstAmount, line.IgstAmount, line.GstRate);
            var returned = soFar.GetValueOrDefault(line.Id, CreditedSoFar.None);
            var quantityLeft = ReturnRules.RemainingQuantity(creditable, returned);
            if (input.Quantity > quantityLeft)
            {
                errors[$"{key}.quantity"] = [quantityLeft > 0
                    ? $"Only {Qty(quantityLeft)} {line.Unit} can still be returned: {Qty(line.Quantity)} billed, {Qty(returned.Quantity)} returned already."
                    : "Everything on this line has been returned already."];
            }
            else if (ReturnRules.RemainingValue(creditable, returned) <= 0)
            {
                errors[$"{key}.quantity"] = ["This line has no value left to return."];
            }
            else
            {
                lines.Add(new DraftLine(line, input.Quantity, ReturnRules.ForGoods(creditable, returned, input.Quantity, bill.IsInterState, withGst)));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }

        var (roundOff, grandTotal) = ReturnRules.Total(lines.Select(l => l.Amounts));
        if (grandTotal <= 0)
        {
            throw new ValidationException("lines", "The return comes to less than ₹1.");
        }

        var balance = bill.GrandTotal - bill.AmountPaid - bill.AmountCredited;
        return new Draft(bill, date, withGst, lines, roundOff, grandTotal, Math.Clamp(balance, 0m, grandTotal));
    }

    private async Task<SupplierReturn> LoadForChangeAsync(int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var ret = await db.SupplierReturns.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier return {id} was not found.");
        db.Entry(ret).Property(r => r.RowVersion).OriginalValue = RowVersions.Parse(rowVersion);
        if (ret.Status == ReturnDocumentStatus.Cancelled)
        {
            throw new ConflictException($"{ret.Number} is cancelled.");
        }

        return ret;
    }

    private string? CancelBlockedReason(SupplierReturn ret)
    {
        var elsewhere = ret.Adjustments.Where(a => a.SupplierBillId != ret.SupplierBillId).Select(a => a.SupplierBill?.Number).Distinct().ToList();
        return Credits.ReturnCancelBlockedReason(ret.Number, ret.Status, ret.ReturnDate, ret.AmountRefunded,
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

    private static decimal Available(SupplierReturn ret) => ret.GrandTotal - ret.AmountAdjusted - ret.AmountRefunded;

    private static string Qty(decimal value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}
