using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface IOrderService
{
    Task<PagedResult<OrderListItemDto>> ListAsync(OrderListRequest request, CancellationToken cancellationToken);
    Task<OrderDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<OrderDto> CreateAsync(OrderSaveRequest request, CancellationToken cancellationToken);
    Task<OrderDto> UpdateAsync(int id, OrderSaveRequest request, CancellationToken cancellationToken);
    Task<OrderDto> QuoteAsync(int id, OrderActionRequest request, CancellationToken cancellationToken);
    Task<OrderDto> ConfirmAsync(int id, OrderConfirmRequest request, CancellationToken cancellationToken);
    Task<OrderDto> CancelAsync(int id, OrderCancelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Quotations and sales orders. Lines are editable while Draft or Quoted; confirming locks them, checks
/// the customer's credit limit and makes the order ready to invoice.
/// </summary>
public sealed class OrderService(
    AppDbContext db,
    IDocumentNumbers numbers,
    ILookupService lookups,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IOrderService
{
    private const string OrderPrefix = "SO";

    public async Task<PagedResult<OrderListItemDto>> ListAsync(OrderListRequest request, CancellationToken cancellationToken)
    {
        var query = db.SalesOrders.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        if (request.From is { } from)
        {
            query = query.Where(o => o.OrderDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(o => o.OrderDate <= to);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(o => o.Number.Contains(q) || o.Customer!.Name.Contains(q) || o.Customer.Code.Contains(q) ||
                                     (o.QuotationNumber != null && o.QuotationNumber.Contains(q)) ||
                                     (o.CustomerReference != null && o.CustomerReference.Contains(q)));
        }

        return await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .Select(o => new OrderListItemDto(
                o.Id, o.Number, o.Status, o.Source, o.OrderDate, o.CustomerId, o.Customer!.Name, o.Lines.Count, o.GrandTotal,
                o.QuotationNumber, o.InvoiceId,
                db.Invoices.Where(i => i.Id == o.InvoiceId).Select(i => i.Number).FirstOrDefault(),
                o.CreatedBy!.FullName))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<OrderDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.CreatedBy)
            .Include(o => o.Lines).ThenInclude(l => l.Product!).ThenInclude(p => p.HsnCode)
            .Include(o => o.Lines).ThenInclude(l => l.Product!).ThenInclude(p => p.Stock)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Order {id} was not found.");

        var invoiceNumber = order.InvoiceId is { } invoiceId
            ? await db.Invoices.Where(i => i.Id == invoiceId).Select(i => i.Number).SingleOrDefaultAsync(cancellationToken)
            : null;

        var companyState = await CompanyStateAsync(cancellationToken);
        var states = await lookups.GetStatesAsync(cancellationToken);
        var isInterState = order.PlaceOfSupplyStateCode != companyState;
        var orderedLines = order.Lines.OrderBy(l => l.LineNumber).ToList();
        var totals = GstCalculator.Calculate(orderedLines.Select(ToGstInput), isInterState);

        var lines = orderedLines.Select((line, index) =>
        {
            var tax = totals.Lines[index];
            return new OrderLineDto(
                line.Id, line.LineNumber, line.ProductId, line.Product!.Sku, line.Product.Name, line.Product.HsnCode!.Code, line.Product.Unit,
                line.Quantity, line.UnitPrice, line.DiscountPercent, line.GstRate, tax.TaxableValue, tax.TaxAmount, tax.LineTotal,
                line.Product.Stock?.QuantityOnHand ?? 0m);
        }).ToList();

        var posState = states.GetValueOrDefault(order.PlaceOfSupplyStateCode);
        return new OrderDto(
            order.Id, order.Number, order.Status, order.Source, order.OrderDate, order.ValidUntil,
            order.CustomerId, order.Customer!.Code, order.Customer.Name, order.Customer.Gstin,
            order.PlaceOfSupplyStateCode, posState?.Name ?? order.PlaceOfSupplyStateCode,
            isInterState, !isInterState && (posState?.UsesUtgst ?? false),
            order.CustomerReference, order.Notes, order.QuotationNumber,
            order.QuotedAtUtc, order.ConfirmedAtUtc, order.CancelledAtUtc, order.CancellationReason,
            order.InvoiceId, invoiceNumber, lines,
            totals.TaxableValue, totals.CgstAmount, totals.SgstAmount, totals.IgstAmount, totals.RoundOff, totals.GrandTotal,
            AmountInWords.Rupees(totals.GrandTotal),
            order.CreatedBy!.FullName, order.CreatedAtUtc, order.UpdatedAtUtc, RowVersions.ToToken(order.RowVersion));
    }

    public async Task<OrderDto> CreateAsync(OrderSaveRequest request, CancellationToken cancellationToken)
    {
        var customer = await LoadCustomerAsync(request.CustomerId, cancellationToken);
        var orderDate = ValidateDates(request.OrderDate, request.ValidUntil);
        var lines = await BuildLinesAsync(request.Lines, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = new SalesOrder
        {
            Number = await numbers.NextAsync(DocumentTypes.SalesOrder, OrderPrefix, orderDate, cancellationToken),
            Source = request.Source,
            CustomerId = customer.Id,
            PlaceOfSupplyStateCode = PlaceOfSupply(customer),
            CreatedByUserId = currentUser.UserId,
        };
        Apply(order, request, orderDate, lines);
        await SetTotalsAsync(order, cancellationToken);

        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(order.Id, cancellationToken);
    }

    public async Task<OrderDto> UpdateAsync(int id, OrderSaveRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Quoted))
        {
            throw new ConflictException($"Order {order.Number} is {order.Status.ToString().ToLowerInvariant()} and can no longer be edited.");
        }

        var customer = await LoadCustomerAsync(request.CustomerId, cancellationToken);
        var orderDate = ValidateDates(request.OrderDate, request.ValidUntil);
        var lines = await BuildLinesAsync(request.Lines, cancellationToken);

        order.CustomerId = customer.Id;
        order.PlaceOfSupplyStateCode = PlaceOfSupply(customer);
        order.Lines.Clear();
        Apply(order, request, orderDate, lines);
        await SetTotalsAsync(order, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<OrderDto> QuoteAsync(int id, OrderActionRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status != SalesOrderStatus.Draft)
        {
            throw new ConflictException($"Only draft orders can be marked as quoted; {order.Number} is {order.Status.ToString().ToLowerInvariant()}.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (order.QuotationNumber is null)
        {
            var prefix = await db.CompanySettings.Select(s => s.QuotationPrefix).SingleAsync(cancellationToken);
            order.QuotationNumber = await numbers.NextAsync(DocumentTypes.Quotation, prefix, clock.Today, cancellationToken);
        }

        order.Status = SalesOrderStatus.Quoted;
        order.QuotedAtUtc = Now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<OrderDto> ConfirmAsync(int id, OrderConfirmRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Quoted))
        {
            throw new ConflictException($"Only draft or quoted orders can be confirmed; {order.Number} is {order.Status.ToString().ToLowerInvariant()}.");
        }

        var customer = await LoadCustomerAsync(order.CustomerId, cancellationToken);
        await EnsureProductsStillActiveAsync(order, cancellationToken);
        await CheckCreditLimitAsync(order, customer, request.OverrideCreditLimit, cancellationToken);

        order.Status = SalesOrderStatus.Confirmed;
        order.ConfirmedAtUtc = Now;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<OrderDto> CancelAsync(int id, OrderCancelRequest request, CancellationToken cancellationToken)
    {
        var order = await LoadForChangeAsync(id, request.RowVersion, cancellationToken);
        if (order.Status is SalesOrderStatus.Invoiced or SalesOrderStatus.Cancelled)
        {
            throw new ConflictException(order.Status == SalesOrderStatus.Invoiced
                ? $"{order.Number} has been invoiced. Cancel the invoice first."
                : $"{order.Number} is already cancelled.");
        }

        order.Status = SalesOrderStatus.Cancelled;
        order.CancelledAtUtc = Now;
        order.CancellationReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    internal static GstLineInput ToGstInput(SalesOrderLine line) => new(line.Quantity, line.UnitPrice, line.DiscountPercent, line.GstRate);

    /// <summary>Goods are taxed where they're delivered: the shipping state, else the billing state.</summary>
    internal static string PlaceOfSupply(Customer customer) => customer.ShippingAddress?.StateCode ?? customer.BillingAddress.StateCode;

    private static void Apply(SalesOrder order, OrderSaveRequest request, DateOnly orderDate, List<SalesOrderLine> lines)
    {
        order.OrderDate = orderDate;
        order.ValidUntil = request.ValidUntil;
        order.CustomerReference = Text.NullIfBlank(request.CustomerReference);
        order.Notes = Text.NullIfBlank(request.Notes);
        order.Lines.AddRange(lines);
    }

    private async Task SetTotalsAsync(SalesOrder order, CancellationToken cancellationToken)
    {
        var isInterState = order.PlaceOfSupplyStateCode != await CompanyStateAsync(cancellationToken);
        var totals = GstCalculator.Calculate(order.Lines.Select(ToGstInput), isInterState);
        order.TaxableValue = totals.TaxableValue;
        order.TaxAmount = totals.TaxAmount;
        order.GrandTotal = totals.GrandTotal;
    }

    private Task<string> CompanyStateAsync(CancellationToken cancellationToken) =>
        db.CompanySettings.AsNoTracking().Select(s => s.StateCode).SingleAsync(cancellationToken);

    private async Task<SalesOrder> LoadForChangeAsync(int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Order {id} was not found.");
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = RowVersions.Parse(rowVersion);
        return order;
    }

    private async Task<Customer> LoadCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        return customer is { IsActive: true }
            ? customer
            : throw new ValidationException("customerId", "Choose an active customer.");
    }

    private DateOnly ValidateDates(DateOnly? requestedOrderDate, DateOnly? validUntil)
    {
        var today = clock.Today;
        var orderDate = requestedOrderDate ?? today;
        if (orderDate > today)
        {
            throw new ValidationException("orderDate", "The order date can't be in the future.");
        }

        if (orderDate < today.AddYears(-1))
        {
            throw new ValidationException("orderDate", "The order date can't be more than a year ago.");
        }

        if (validUntil is { } until && until < orderDate)
        {
            throw new ValidationException("validUntil", "'Valid until' can't be before the order date.");
        }

        return orderDate;
    }

    /// <summary>Products must exist, be active and appear once; prices default to the list price; GST rate is copied from the HSN code.</summary>
    private async Task<List<SalesOrderLine>> BuildLinesAsync(List<OrderLineInput> inputs, CancellationToken cancellationToken)
    {
        var ids = inputs.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.IsActive, p.ListPrice, GstRate = p.HsnCode!.GstRate })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var seen = new HashSet<int>();
        var lines = new List<SalesOrderLine>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (!products.TryGetValue(input.ProductId, out var product) || !product.IsActive)
            {
                errors[$"lines[{i}].productId"] = ["Choose an active product."];
                continue;
            }

            if (!seen.Add(input.ProductId))
            {
                errors[$"lines[{i}].productId"] = ["This product is already on the order. Change the quantity on the existing line instead."];
                continue;
            }

            lines.Add(new SalesOrderLine
            {
                LineNumber = lines.Count + 1,
                ProductId = input.ProductId,
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice ?? product.ListPrice,
                DiscountPercent = input.DiscountPercent,
                GstRate = product.GstRate,
            });
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some lines need attention.", errors);
        }

        return lines;
    }

    private async Task EnsureProductsStillActiveAsync(SalesOrder order, CancellationToken cancellationToken)
    {
        var ids = order.Lines.Select(l => l.ProductId).ToList();
        var inactive = await db.Products.Where(p => ids.Contains(p.Id) && !p.IsActive).Select(p => p.Sku).ToListAsync(cancellationToken);
        if (inactive.Count > 0)
        {
            throw new ValidationException("lines", $"These products are no longer active: {string.Join(", ", inactive)}. Edit the order first.");
        }
    }

    /// <summary>
    /// Exposure = unpaid invoices + other confirmed orders + this order. Over the limit, sales reps are
    /// stopped; managers and admins can confirm anyway by setting OverrideCreditLimit.
    /// </summary>
    private async Task CheckCreditLimitAsync(SalesOrder order, Customer customer, bool overrideRequested, CancellationToken cancellationToken)
    {
        if (customer.CreditLimit is not { } limit)
        {
            return;
        }

        var outstanding = await db.Invoices
            .Where(i => i.CustomerId == customer.Id && i.Status != InvoiceStatus.Cancelled)
            .SumAsync(i => (decimal?)(i.GrandTotal - i.AmountPaid), cancellationToken) ?? 0m;
        var otherOrders = await db.SalesOrders
            .Where(o => o.CustomerId == customer.Id && o.Status == SalesOrderStatus.Confirmed && o.Id != order.Id)
            .SumAsync(o => (decimal?)o.GrandTotal, cancellationToken) ?? 0m;
        var exposure = outstanding + otherOrders + order.GrandTotal;

        if (exposure <= limit || (overrideRequested && currentUser.Has(Permissions.CreditLimitOverride)))
        {
            return;
        }

        var what = $"This order takes {customer.Name} to ₹{AmountInWords.IndianGrouping(exposure)} against a credit limit of " +
                   $"₹{AmountInWords.IndianGrouping(limit)} (unpaid invoices ₹{AmountInWords.IndianGrouping(outstanding)}, " +
                   $"other confirmed orders ₹{AmountInWords.IndianGrouping(otherOrders)}).";
        throw new ValidationException("creditLimit", currentUser.Has(Permissions.CreditLimitOverride)
            ? $"{what} Confirm with override to go ahead."
            : $"{what} Ask someone who can override credit limits to approve it.");
    }
}
