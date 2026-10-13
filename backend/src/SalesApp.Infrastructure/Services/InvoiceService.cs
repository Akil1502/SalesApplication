using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceListItemDto>> ListAsync(InvoiceListRequest request, CancellationToken cancellationToken);
    Task<InvoiceSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
    Task<InvoiceDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<InvoiceDto> CreateAsync(InvoiceCreateRequest request, CancellationToken cancellationToken);
    Task<InvoiceDto> CancelAsync(int id, InvoiceCancelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Tax invoices. Issuing one is a single transaction: allocate the next gapless number, snapshot seller,
/// buyer and products, recalculate GST at today's rates, take the goods out of stock, and mark the
/// order invoiced. Any failure (say, not enough stock) rolls all of it back, number included.
/// </summary>
public sealed class InvoiceService(
    AppDbContext db,
    IDocumentNumbers numbers,
    IStockLedger ledger,
    ILookupService lookups,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IInvoiceService
{
    private static readonly InvoiceStatus[] Open = [InvoiceStatus.Unpaid, InvoiceStatus.PartiallyPaid];

    public async Task<PagedResult<InvoiceListItemDto>> ListAsync(InvoiceListRequest request, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var query = db.Invoices.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        if (request.OverdueOnly)
        {
            query = query.Where(i => Open.Contains(i.Status) && i.DueDate < today);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(i => i.CustomerId == customerId);
        }

        if (request.From is { } from)
        {
            query = query.Where(i => i.InvoiceDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(i => i.InvoiceDate <= to);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(i => i.Number.Contains(q) || i.CustomerName.Contains(q) ||
                                     (i.CustomerGstin != null && i.CustomerGstin.Contains(q)));
        }

        var page = await query
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Select(i => new { i.Id, i.Number, i.InvoiceDate, i.DueDate, i.CustomerId, i.CustomerName, i.GrandTotal, i.AmountPaid, i.Status })
            .ToPagedResultAsync(request, cancellationToken);

        var items = page.Items
            .Select(i => new InvoiceListItemDto(i.Id, i.Number, i.InvoiceDate, i.DueDate, i.CustomerId, i.CustomerName,
                i.GrandTotal, i.AmountPaid, i.Status == InvoiceStatus.Cancelled ? 0m : i.GrandTotal - i.AmountPaid, i.Status,
                DaysOverdue(i.Status, i.DueDate, today)))
            .ToList();
        return new PagedResult<InvoiceListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<InvoiceSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var open = db.Invoices.AsNoTracking().Where(i => Open.Contains(i.Status));

        var outstanding = await open.SumAsync(i => (decimal?)(i.GrandTotal - i.AmountPaid), cancellationToken) ?? 0m;
        var outstandingCount = await open.CountAsync(cancellationToken);
        var overdue = open.Where(i => i.DueDate < today);
        var overdueAmount = await overdue.SumAsync(i => (decimal?)(i.GrandTotal - i.AmountPaid), cancellationToken) ?? 0m;
        var overdueCount = await overdue.CountAsync(cancellationToken);
        var invoiced = await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= monthStart && i.InvoiceDate <= today)
            .SumAsync(i => (decimal?)i.GrandTotal, cancellationToken) ?? 0m;
        var collected = await db.Payments.Where(p => p.PaymentDate >= monthStart && p.PaymentDate <= today)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new InvoiceSummaryDto(outstanding, outstandingCount, overdueAmount, overdueCount, invoiced, collected);
    }

    public async Task<InvoiceDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Payments).ThenInclude(p => p.CreatedBy)
            .Include(i => i.CreatedBy)
            .Include(i => i.SalesOrder)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Invoice {id} was not found.");

        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
        var lines = invoice.Lines.OrderBy(l => l.LineNumber).ToList();

        var hsnSummary = lines
            .GroupBy(l => new { l.HsnCode, l.GstRate })
            .OrderBy(g => g.Key.HsnCode).ThenBy(g => g.Key.GstRate)
            .Select(g => new HsnSummaryDto(g.Key.HsnCode, g.Key.GstRate, g.Sum(l => l.TaxableValue),
                g.Sum(l => l.CgstAmount), g.Sum(l => l.SgstAmount), g.Sum(l => l.IgstAmount)))
            .ToList();

        var payments = invoice.Payments.OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .Select(p => new PaymentDto(p.Id, invoice.Id, invoice.Number, invoice.CustomerId, invoice.CustomerName, p.PaymentDate, p.Amount, p.Mode,
                p.Reference, p.Notes, p.CreatedBy!.FullName, p.CreatedAtUtc))
            .ToList();

        static InvoiceAddressDto Address(InvoiceAddress a) => new(a.Line1, a.Line2, a.City, a.StateCode, a.StateName, a.Pincode);

        return new InvoiceDto(
            invoice.Id, invoice.Number, FinancialYear.Label(invoice.InvoiceDate), invoice.InvoiceDate, invoice.DueDate, invoice.Status,
            invoice.SalesOrderId, invoice.SalesOrder!.Number,
            invoice.SellerName, invoice.SellerGstin, invoice.SellerStateCode, invoice.SellerAddress,
            invoice.CustomerId, invoice.CustomerName, invoice.CustomerGstin,
            Address(invoice.BillingAddress), invoice.ShippingAddress is null ? null : Address(invoice.ShippingAddress),
            invoice.PlaceOfSupplyStateCode, invoice.PlaceOfSupplyStateName, invoice.IsInterState, invoice.UsesUtgst, invoice.CustomerReference,
            [.. lines.Select(l => new InvoiceLineDto(l.LineNumber, l.ProductId, l.ProductSku, l.ProductName, l.HsnCode, l.Unit,
                l.Quantity, l.UnitPrice, l.DiscountPercent, l.GrossAmount, l.DiscountAmount, l.TaxableValue, l.GstRate,
                l.CgstRate, l.CgstAmount, l.SgstRate, l.SgstAmount, l.IgstRate, l.IgstAmount, l.LineTotal))],
            hsnSummary,
            invoice.TaxableValue, invoice.CgstAmount, invoice.SgstAmount, invoice.IgstAmount, invoice.RoundOff, invoice.GrandTotal,
            invoice.AmountPaid, invoice.Status == InvoiceStatus.Cancelled ? 0m : invoice.GrandTotal - invoice.AmountPaid,
            DaysOverdue(invoice.Status, invoice.DueDate, clock.Today),
            AmountInWords.Rupees(invoice.GrandTotal),
            payments, invoice.CancelledAtUtc, invoice.CancellationReason,
            settings.BankName, settings.BankAccountNumber, settings.BankIfsc, settings.BankBranch, settings.InvoiceTerms,
            invoice.CreatedBy!.FullName, invoice.CreatedAtUtc);
    }

    public async Task<InvoiceDto> CreateAsync(InvoiceCreateRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.SalesOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken)
            ?? throw new ValidationException("salesOrderId", "Choose a valid order.");
        if (order.Status != SalesOrderStatus.Confirmed)
        {
            throw new ConflictException($"Only confirmed orders can be invoiced; {order.Number} is {order.Status.ToString().ToLowerInvariant()}.");
        }

        var today = clock.Today;
        var invoiceDate = request.InvoiceDate ?? today;
        if (invoiceDate > today)
        {
            throw new ValidationException("invoiceDate", "The invoice date can't be in the future.");
        }

        if (invoiceDate < order.OrderDate)
        {
            throw new ValidationException("invoiceDate", "The invoice date can't be before the order date.");
        }

        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == order.CustomerId, cancellationToken);
        var states = await lookups.GetStatesAsync(cancellationToken);

        // Allocating the number takes the financial year's lock, so the date check below can't race
        // with another invoice being issued at the same moment.
        var number = await numbers.NextAsync(DocumentTypes.Invoice, settings.InvoicePrefix, invoiceDate, cancellationToken);
        var financialYear = FinancialYear.StartYear(invoiceDate);
        var latest = await db.Invoices.Where(i => i.FinancialYear == financialYear).MaxAsync(i => (DateOnly?)i.InvoiceDate, cancellationToken);
        if (latest is { } latestDate && invoiceDate < latestDate)
        {
            throw new ValidationException("invoiceDate",
                $"Invoices are numbered in date order. The latest invoice this financial year is dated {latestDate:dd MMM yyyy}, so this one can't be earlier.");
        }

        var productIds = order.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.HsnCode)
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var isInterState = order.PlaceOfSupplyStateCode != settings.StateCode;
        var posState = states[order.PlaceOfSupplyStateCode];
        var orderLines = order.Lines.OrderBy(l => l.LineNumber).ToList();

        // Tax at the rate in force on the invoice date (today's HSN rate), not the rate when quoted.
        var totals = GstCalculator.Calculate(
            orderLines.Select(l => new GstLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent, products[l.ProductId].HsnCode!.GstRate)),
            isInterState);

        var invoice = new Invoice
        {
            Number = number,
            FinancialYear = financialYear,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(customer.PaymentTermsDays),
            SalesOrderId = order.Id,
            CustomerId = customer.Id,
            SellerName = settings.LegalName,
            SellerGstin = settings.Gstin,
            SellerStateCode = settings.StateCode,
            SellerAddress = SellerAddress(settings, states),
            CustomerName = customer.Name,
            CustomerGstin = customer.Gstin,
            BillingAddress = Snapshot(customer.BillingAddress, states),
            ShippingAddress = customer.ShippingAddress is null ? null : Snapshot(customer.ShippingAddress, states),
            PlaceOfSupplyStateCode = posState.Code,
            PlaceOfSupplyStateName = posState.Name,
            IsInterState = isInterState,
            UsesUtgst = !isInterState && posState.UsesUtgst,
            CustomerReference = order.CustomerReference,
            TaxableValue = totals.TaxableValue,
            CgstAmount = totals.CgstAmount,
            SgstAmount = totals.SgstAmount,
            IgstAmount = totals.IgstAmount,
            RoundOff = totals.RoundOff,
            GrandTotal = totals.GrandTotal,
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = Now,
        };

        for (var i = 0; i < orderLines.Count; i++)
        {
            var line = orderLines[i];
            var product = products[line.ProductId];
            var tax = totals.Lines[i];
            invoice.Lines.Add(new InvoiceLine
            {
                LineNumber = i + 1,
                ProductId = product.Id,
                ProductSku = product.Sku,
                ProductName = product.Name,
                HsnCode = product.HsnCode!.Code,
                Unit = product.Unit,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                GrossAmount = tax.GrossAmount,
                DiscountAmount = tax.DiscountAmount,
                TaxableValue = tax.TaxableValue,
                GstRate = product.HsnCode.GstRate,
                CgstRate = tax.CgstRate,
                CgstAmount = tax.CgstAmount,
                SgstRate = tax.SgstRate,
                SgstAmount = tax.SgstAmount,
                IgstRate = tax.IgstRate,
                IgstAmount = tax.IgstAmount,
                LineTotal = tax.LineTotal,
            });
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        // Goods leave stock with the invoice. Not enough stock aborts the whole invoice.
        foreach (var line in invoice.Lines)
        {
            await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.Sale, -line.Quantity, invoiceDate,
                ReferenceType: StockReferenceTypes.Invoice, ReferenceId: invoice.Id, ReferenceNumber: invoice.Number), cancellationToken);
        }

        order.Status = SalesOrderStatus.Invoiced;
        order.InvoiceId = invoice.Id;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(invoice.Id, cancellationToken);
    }

    public async Task<InvoiceDto> CancelAsync(int id, InvoiceCancelRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var invoice = await db.Invoices.Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Invoice {id} was not found.");
        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            throw new ConflictException($"{invoice.Number} is already cancelled.");
        }

        if (invoice.AmountPaid > 0)
        {
            throw new ConflictException($"{invoice.Number} has payments recorded against it, so it can't be cancelled. A credit note is needed instead (not yet supported).");
        }

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancelledAtUtc = Now;
        invoice.CancellationReason = request.Reason.Trim();

        foreach (var line in invoice.Lines)
        {
            await ledger.PostAsync(new StockPosting(
                line.ProductId, StockMovementType.SaleReversal, line.Quantity, clock.Today,
                ReferenceType: StockReferenceTypes.Invoice, ReferenceId: invoice.Id, ReferenceNumber: invoice.Number,
                Notes: $"Invoice cancelled: {invoice.CancellationReason}"), cancellationToken);
        }

        // The order goes back to Confirmed so it can be invoiced again or cancelled.
        var order = await db.SalesOrders.SingleAsync(o => o.Id == invoice.SalesOrderId, cancellationToken);
        order.Status = SalesOrderStatus.Confirmed;
        order.InvoiceId = null;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    internal static int? DaysOverdue(InvoiceStatus status, DateOnly dueDate, DateOnly today) =>
        Open.Contains(status) && today > dueDate ? today.DayNumber - dueDate.DayNumber : null;

    private static InvoiceAddress Snapshot(Address address, IReadOnlyDictionary<string, Core.Contracts.Masters.StateDto> states) => new()
    {
        Line1 = address.Line1,
        Line2 = address.Line2,
        City = address.City,
        StateCode = address.StateCode,
        StateName = states.GetValueOrDefault(address.StateCode)?.Name ?? address.StateCode,
        Pincode = address.Pincode,
    };

    private static string SellerAddress(CompanySettings s, IReadOnlyDictionary<string, Core.Contracts.Masters.StateDto> states)
    {
        var state = states.GetValueOrDefault(s.StateCode)?.Name ?? s.StateCode;
        var parts = new[] { s.AddressLine1, s.AddressLine2, $"{s.City} - {s.Pincode}", state }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }
}
