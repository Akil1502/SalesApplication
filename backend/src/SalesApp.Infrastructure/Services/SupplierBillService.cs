using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Purchasing;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ISupplierBillService
{
    Task<PagedResult<SupplierBillListItemDto>> ListAsync(SupplierBillListRequest request, CancellationToken cancellationToken);
    Task<SupplierBillSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
    Task<SupplierBillDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<SupplierBillDto> CreateAsync(SupplierBillCreateRequest request, CancellationToken cancellationToken);
    Task<SupplierBillDto> ApproveAsync(int id, SupplierBillApproveRequest request, CancellationToken cancellationToken);
    Task<SupplierBillDto> CancelAsync(int id, SupplierBillCancelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Supplier bills. Entering one is a single transaction: allocate our number, work out GST (IGST for a supplier in
/// another state), run the three-way check against the order and what was received, add the bill's price differences
/// and charges to the cost of the stock still on hand, and count the quantities as billed on the order.
/// </summary>
public sealed class SupplierBillService(
    AppDbContext db,
    IDocumentNumbers numbers,
    ILookupService lookups,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ISupplierBillService
{
    private const string BillPrefix = "PB";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly SupplierBillStatus[] Open = [SupplierBillStatus.Unpaid, SupplierBillStatus.PartiallyPaid];

    /// <summary>Orders a bill can be entered against: placed ones, whether or not everything has arrived.</summary>
    private static readonly PurchaseOrderStatus[] Billable =
        [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived, PurchaseOrderStatus.Received, PurchaseOrderStatus.Closed];

    public async Task<PagedResult<SupplierBillListItemDto>> ListAsync(SupplierBillListRequest request, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var query = db.SupplierBills.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(b => b.Status == status);
        }

        if (request.MatchStatus is { } match)
        {
            query = query.Where(b => b.MatchStatus == match && b.Status != SupplierBillStatus.Cancelled);
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(b => b.SupplierId == supplierId);
        }

        if (request.PurchaseOrderId is { } orderId)
        {
            query = query.Where(b => b.PurchaseOrderId == orderId);
        }

        if (request.From is { } from)
        {
            query = query.Where(b => b.BillDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(b => b.BillDate <= to);
        }

        if (request.UnpaidOnly || request.OverdueOnly)
        {
            query = query.Where(b => Open.Contains(b.Status));
        }

        if (request.OverdueOnly)
        {
            query = query.Where(b => b.DueDate < today);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(b => b.Number.Contains(q) || b.SupplierInvoiceNumber.Contains(q) || b.SupplierName.Contains(q) ||
                                     b.PurchaseOrder!.Number.Contains(q) || (b.SupplierGstin != null && b.SupplierGstin.Contains(q)));
        }

        var page = await query
            .OrderByDescending(b => b.BillDate)
            .ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.Id, b.Number, b.SupplierInvoiceNumber, b.BillDate, b.DueDate, b.SupplierId, b.SupplierName, b.PurchaseOrderId,
                OrderNumber = b.PurchaseOrder!.Number, b.GrandTotal, b.AmountPaid, b.Status, b.MatchStatus,
            })
            .ToPagedResultAsync(request, cancellationToken);

        var items = page.Items
            .Select(b => new SupplierBillListItemDto(b.Id, b.Number, b.SupplierInvoiceNumber, b.BillDate, b.DueDate, b.SupplierId, b.SupplierName,
                b.PurchaseOrderId, b.OrderNumber, b.GrandTotal, b.AmountPaid, b.Status == SupplierBillStatus.Cancelled ? 0m : b.GrandTotal - b.AmountPaid,
                b.Status, b.MatchStatus, DaysOverdue(b.Status, b.DueDate, today)))
            .ToList();
        return new PagedResult<SupplierBillListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<SupplierBillSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var open = db.SupplierBills.AsNoTracking().Where(b => Open.Contains(b.Status));

        var payable = await open.SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid), cancellationToken) ?? 0m;
        var openCount = await open.CountAsync(cancellationToken);
        var overdue = open.Where(b => b.DueDate < today);
        var overdueAmount = await overdue.SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid), cancellationToken) ?? 0m;
        var overdueCount = await overdue.CountAsync(cancellationToken);
        var weekAhead = today.AddDays(7);
        var dueSoon = await open.Where(b => b.DueDate >= today && b.DueDate <= weekAhead).SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid), cancellationToken) ?? 0m;
        var needsApproval = await db.SupplierBills.CountAsync(b => b.MatchStatus == BillMatchStatus.NeedsApproval && b.Status != SupplierBillStatus.Cancelled, cancellationToken);
        var paid = await db.SupplierPayments.Where(p => p.PaymentDate >= monthStart && p.PaymentDate <= today).SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new SupplierBillSummaryDto(payable, openCount, overdueAmount, overdueCount, dueSoon, needsApproval, paid);
    }

    public async Task<SupplierBillDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var bill = await db.SupplierBills
            .AsNoTracking()
            .Include(b => b.Lines)
            .Include(b => b.Payments).ThenInclude(p => p.CreatedBy)
            .Include(b => b.PurchaseOrder)
            .Include(b => b.CreatedBy)
            .Include(b => b.ApprovedBy)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier bill {id} was not found.");

        var states = await lookups.GetStatesAsync(cancellationToken);
        var payments = bill.Payments.OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .Select(p => new SupplierPaymentDto(p.Id, bill.Id, bill.Number, bill.SupplierInvoiceNumber, bill.SupplierId, bill.SupplierName,
                p.PaymentDate, p.Amount, p.Mode, p.Reference, p.Notes, p.CreatedBy!.FullName, p.CreatedAtUtc))
            .ToList();

        return new SupplierBillDto(
            bill.Id, bill.Number, FinancialYear.Label(bill.BillDate), bill.SupplierInvoiceNumber, bill.BillDate, bill.DueDate,
            bill.Status, bill.MatchStatus, bill.Source,
            bill.SupplierId, bill.SupplierName, bill.SupplierGstin, bill.SupplierStateCode,
            states.GetValueOrDefault(bill.SupplierStateCode)?.Name ?? bill.SupplierStateCode, bill.IsInterState,
            bill.PurchaseOrderId, bill.PurchaseOrder!.Number,
            [.. bill.Lines.OrderBy(l => l.LineNumber).Select(l => new SupplierBillLineDto(l.LineNumber, l.PurchaseOrderLineId, l.ProductId,
                l.ProductSku, l.ProductName, l.HsnCode, l.Unit, l.Quantity, l.UnitPrice, l.OrderedPrice, l.TaxableValue, l.GstRate,
                l.CgstAmount, l.SgstAmount, l.IgstAmount, l.LineTotal, l.Issue))],
            bill.TaxableValue, bill.ChargesAmount, bill.ChargesGstRate, bill.ChargesDescription,
            bill.CgstAmount, bill.SgstAmount, bill.IgstAmount, bill.RoundOff, bill.GrandTotal, bill.SupplierTotal,
            bill.AmountPaid, bill.Status == SupplierBillStatus.Cancelled ? 0m : bill.GrandTotal - bill.AmountPaid,
            DaysOverdue(bill.Status, bill.DueDate, clock.Today),
            bill.MatchIssuesJson is null ? [] : JsonSerializer.Deserialize<List<string>>(bill.MatchIssuesJson, Json) ?? [],
            bill.ApprovedBy?.FullName, bill.ApprovedAtUtc, bill.ApprovalNote, bill.Notes, payments,
            bill.CancelledAtUtc, bill.CancellationReason, bill.CreatedBy!.FullName, bill.CreatedAtUtc, RowVersions.ToToken(bill.RowVersion));
    }

    public async Task<SupplierBillDto> CreateAsync(SupplierBillCreateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.PurchaseOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new ValidationException("purchaseOrderId", "Choose a valid purchase order.");
        if (!Billable.Contains(order.Status))
        {
            throw new ConflictException(order.Status == PurchaseOrderStatus.Cancelled
                ? $"{order.Number} is cancelled, so no bill can be entered against it."
                : $"{order.Number} hasn't been placed with the supplier yet, so no bill can be entered against it.");
        }

        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == order.SupplierId, cancellationToken);
        var invoiceNumber = request.SupplierInvoiceNumber.Trim();
        var duplicate = await db.SupplierBills
            .Where(b => b.SupplierId == supplier.Id && b.SupplierInvoiceNumber == invoiceNumber && b.Status != SupplierBillStatus.Cancelled)
            .Select(b => b.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
        {
            throw new ValidationException("supplierInvoiceNumber", $"{supplier.Name}'s invoice {invoiceNumber} is already entered as {duplicate}.");
        }

        var today = clock.Today;
        var billDate = request.BillDate ?? today;
        if (billDate > today)
        {
            throw new ValidationException("billDate", "The bill date can't be in the future.");
        }

        if (billDate < today.AddYears(-1))
        {
            throw new ValidationException("billDate", "The bill date can't be more than a year ago.");
        }

        var dueDate = request.DueDate ?? billDate.AddDays(supplier.PaymentTermsDays);
        if (dueDate < billDate)
        {
            throw new ValidationException("dueDate", "The due date can't be before the bill date.");
        }

        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
        var productIds = order.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.HsnCode)
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        // Lines: on this order, each once.
        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<int>();
        var picked = new List<(SupplierBillLineInput Input, PurchaseOrderLine OrderLine, decimal GstRate)>();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var input = request.Lines[i];
            var orderLine = order.Lines.FirstOrDefault(l => l.Id == input.PurchaseOrderLineId);
            if (orderLine is null)
            {
                errors[$"lines[{i}].purchaseOrderLineId"] = [$"That line isn't on {order.Number}."];
            }
            else if (!seen.Add(orderLine.Id))
            {
                errors[$"lines[{i}].purchaseOrderLineId"] = ["This line is on the bill twice. Combine the quantities."];
            }
            else
            {
                picked.Add((input, orderLine, input.GstRate ?? orderLine.GstRate));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }

        var isInterState = supplier.Address.StateCode != settings.StateCode;
        var gstLines = picked.Select(p => new GstLineInput(p.Input.Quantity, p.Input.UnitPrice, 0m, p.GstRate)).ToList();
        if (request.ChargesAmount > 0)
        {
            gstLines.Add(new GstLineInput(1m, request.ChargesAmount, 0m, request.ChargesGstRate));
        }

        var totals = GstCalculator.Calculate(gstLines, isInterState);
        var lineTaxable = picked.Select((_, i) => totals.Lines[i].TaxableValue).Sum();

        var bill = new SupplierBill
        {
            Number = await numbers.NextAsync(DocumentTypes.SupplierBill, BillPrefix, billDate, cancellationToken),
            FinancialYear = FinancialYear.StartYear(billDate),
            SupplierInvoiceNumber = invoiceNumber,
            BillDate = billDate,
            DueDate = dueDate,
            Source = request.Source,
            SupplierId = supplier.Id,
            PurchaseOrderId = order.Id,
            SupplierName = supplier.Name,
            SupplierGstin = supplier.Gstin,
            SupplierStateCode = supplier.Address.StateCode,
            IsInterState = isInterState,
            TaxableValue = lineTaxable,
            ChargesAmount = request.ChargesAmount,
            ChargesGstRate = request.ChargesAmount > 0 ? request.ChargesGstRate : 0m,
            ChargesDescription = request.ChargesAmount > 0 ? Text.NullIfBlank(request.ChargesDescription) ?? "Freight and other charges" : null,
            CgstAmount = totals.CgstAmount,
            SgstAmount = totals.SgstAmount,
            IgstAmount = totals.IgstAmount,
            RoundOff = totals.RoundOff,
            GrandTotal = totals.GrandTotal,
            SupplierTotal = request.SupplierTotal,
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        };

        var issues = new List<string>();
        for (var i = 0; i < picked.Count; i++)
        {
            var (input, orderLine, gstRate) = picked[i];
            var product = products[orderLine.ProductId];
            var tax = totals.Lines[i];
            var issue = BillMatching.LineIssue(new BillLineCheck(input.Quantity, input.UnitPrice, gstRate, orderLine.UnitPrice, product.HsnCode!.GstRate,
                orderLine.QuantityReceived, orderLine.QuantityBilled, product.Unit.ToString()), settings.BillPriceTolerancePercent);
            bill.Lines.Add(new SupplierBillLine
            {
                LineNumber = i + 1,
                PurchaseOrderLineId = orderLine.Id,
                ProductId = product.Id,
                ProductSku = product.Sku,
                ProductName = product.Name,
                HsnCode = product.HsnCode.Code,
                Unit = product.Unit,
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                OrderedPrice = orderLine.UnitPrice,
                TaxableValue = tax.TaxableValue,
                GstRate = gstRate,
                CgstRate = tax.CgstRate,
                CgstAmount = tax.CgstAmount,
                SgstRate = tax.SgstRate,
                SgstAmount = tax.SgstAmount,
                IgstRate = tax.IgstRate,
                IgstAmount = tax.IgstAmount,
                LineTotal = tax.LineTotal,
                Issue = issue,
            });
        }

        if (BillMatching.TotalIssue(bill.GrandTotal, bill.SupplierTotal) is { } totalIssue)
        {
            issues.Add(totalIssue);
        }

        if (supplier.Gstin is null && bill.CgstAmount + bill.SgstAmount + bill.IgstAmount > 0)
        {
            issues.Add($"{supplier.Name} has no GSTIN on record, so the GST on this bill can't be claimed as input credit.");
        }

        bill.MatchIssuesJson = issues.Count > 0 ? JsonSerializer.Serialize(issues, Json) : null;
        bill.MatchStatus = issues.Count > 0 || bill.Lines.Any(l => l.Issue is not null) ? BillMatchStatus.NeedsApproval : BillMatchStatus.Matched;

        db.SupplierBills.Add(bill);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var line in bill.Lines)
        {
            order.Lines.Single(l => l.Id == line.PurchaseOrderLineId).QuantityBilled += line.Quantity;
        }

        await AdjustCostsAsync(bill, sign: 1, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(bill.Id, cancellationToken);
    }

    public async Task<SupplierBillDto> ApproveAsync(int id, SupplierBillApproveRequest request, CancellationToken cancellationToken)
    {
        var bill = await db.SupplierBills.SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier bill {id} was not found.");
        db.Entry(bill).Property(b => b.RowVersion).OriginalValue = RowVersions.Parse(request.RowVersion);

        if (bill.Status == SupplierBillStatus.Cancelled)
        {
            throw new ConflictException($"{bill.Number} is cancelled.");
        }

        if (bill.MatchStatus != BillMatchStatus.NeedsApproval)
        {
            throw new ConflictException($"{bill.Number} doesn't need approval.");
        }

        bill.MatchStatus = BillMatchStatus.Approved;
        bill.ApprovedByUserId = currentUser.UserId;
        bill.ApprovedAtUtc = Now;
        bill.ApprovalNote = request.Note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SupplierBillDto> CancelAsync(int id, SupplierBillCancelRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var bill = await db.SupplierBills.Include(b => b.Lines).SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier bill {id} was not found.");
        if (bill.Status == SupplierBillStatus.Cancelled)
        {
            throw new ConflictException($"{bill.Number} is already cancelled.");
        }

        if (bill.AmountPaid > 0)
        {
            throw new ConflictException($"Payments are recorded against {bill.Number}, so it can't be cancelled.");
        }

        bill.Status = SupplierBillStatus.Cancelled;
        bill.CancelledAtUtc = Now;
        bill.CancellationReason = request.Reason.Trim();

        var orderLineIds = bill.Lines.Select(l => l.PurchaseOrderLineId).ToList();
        var orderLines = await db.PurchaseOrderLines.Where(l => orderLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        foreach (var line in bill.Lines)
        {
            var orderLine = orderLines[line.PurchaseOrderLineId];
            orderLine.QuantityBilled = Math.Max(0m, orderLine.QuantityBilled - line.Quantity);
        }

        await AdjustCostsAsync(bill, sign: -1, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    internal static int? DaysOverdue(SupplierBillStatus status, DateOnly dueDate, DateOnly today) =>
        Open.Contains(status) && today > dueDate ? today.DayNumber - dueDate.DayNumber : null;

    /// <summary>
    /// What the bill adds to each product's cost beyond the order price its goods were received at: the price
    /// difference on the billed quantity, plus its share of the charges by line value. Applied to the stock still on
    /// hand; cancelling the bill applies the same amounts with the sign reversed.
    /// </summary>
    private async Task AdjustCostsAsync(SupplierBill bill, int sign, CancellationToken cancellationToken)
    {
        var lines = bill.Lines.OrderBy(l => l.LineNumber).ToList();
        var charges = PurchaseCosting.Allocate(bill.ChargesAmount, [.. lines.Select(l => l.TaxableValue)]);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var extra = (line.UnitPrice - line.OrderedPrice) * line.Quantity + charges[i];
            await StockCosting.ApplyBillAdjustmentAsync(db, line.ProductId, line.Quantity, sign * extra, cancellationToken);
        }
    }
}
