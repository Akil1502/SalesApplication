using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SalesApp.AI.AskData;
using SalesApp.AI.OrderEntry;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Assistant;

/// <summary>A tool's answer for Claude, and how the lookup is labelled in the conversation view.</summary>
public sealed record ToolOutcome(string Content, bool IsError, string Label, string? Detail = null);

/// <summary>
/// The assistant's read-only lookups. Each returns compact JSON for Claude (amounts as numbers, dates as
/// yyyy-MM-dd) and never includes customers' or suppliers' phone numbers or emails. Errors are written for Claude to
/// act on. Each lookup needs the permission for what it reads (customers, suppliers, invoices, orders, purchase
/// orders, bills, products, or Ask your data for SQL), the same as the screens; without it the tool answers with an
/// error Claude passes on. The tools stay in the request either way, because removing one would change the
/// conversation's cached prefix. Product cost prices are never returned.
/// </summary>
public sealed class AssistantTools(AppDbContext db, IGuardedQueryRunner queries, IBusinessClock clock, ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly InvoiceStatus[] Open = [InvoiceStatus.Unpaid, InvoiceStatus.PartiallyPaid];
    private static readonly SupplierBillStatus[] OpenBills = [SupplierBillStatus.Unpaid, SupplierBillStatus.PartiallyPaid];
    private static readonly PurchaseOrderStatus[] Receivable = [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived];
    private static readonly PurchaseOrderStatus[] OpenOrders = [PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived];

    /// <summary>The permission each lookup needs, and what to call the data when it's missing.</summary>
    private static readonly Dictionary<string, (string Permission, string What)> Needs = new()
    {
        [AssistantPrompt.SearchCustomers] = (Permissions.CustomersView, "customers"),
        [AssistantPrompt.GetCustomer] = (Permissions.CustomersView, "customers"),
        [AssistantPrompt.ListInvoices] = (Permissions.InvoicesView, "invoices"),
        [AssistantPrompt.SearchProducts] = (Permissions.ProductsView, "products"),
        [AssistantPrompt.SearchSuppliers] = (Permissions.SuppliersView, "suppliers"),
        [AssistantPrompt.GetSupplier] = (Permissions.SuppliersView, "suppliers"),
        [AssistantPrompt.QuerySalesData] = (Permissions.AskDataUse, "sales data (Ask your data)"),
        [AssistantPrompt.CreateDraft] = (Permissions.CustomersView, "customers' contact details"),
    };

    /// <summary>The refusal for a lookup the user isn't allowed, or null when they are. A draft to a supplier needs suppliers.</summary>
    public ToolOutcome? Refusal(string name, IReadOnlyDictionary<string, JsonElement>? input = null)
    {
        if (name == AssistantPrompt.CreateDraft && input is not null && Number(input, "supplierId") > 0)
        {
            return currentUser.Has(Permissions.SuppliersView) ? null : NoAccess("suppliers' contact details");
        }

        return Needs.TryGetValue(name, out var need) && !currentUser.Has(need.Permission) ? NoAccess(need.What) : null;
    }

    public static ToolOutcome NoAccess(string what) => new(
        $"This user doesn't have permission to see {what} in SalesApp, so you can't look that up for them. Tell them, " +
        "and that an administrator can give them access under Users & roles.",
        true,
        $"No access to {what}");

    public async Task<ToolOutcome> RunAsync(Guid conversationId, string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken) =>
        Refusal(name) ?? name switch
        {
            AssistantPrompt.SearchCustomers => await SearchCustomersAsync(Text(input, "query"), cancellationToken),
            AssistantPrompt.GetCustomer => await GetCustomerAsync(Number(input, "customerId"), cancellationToken),
            AssistantPrompt.ListInvoices => await ListInvoicesAsync(Number(input, "customerId"), Text(input, "status"), cancellationToken),
            AssistantPrompt.GetDocument => await GetDocumentAsync(Text(input, "number"), cancellationToken),
            AssistantPrompt.SearchProducts => await SearchProductsAsync(Text(input, "query"), cancellationToken),
            AssistantPrompt.SearchSuppliers => await SearchSuppliersAsync(Text(input, "query"), cancellationToken),
            AssistantPrompt.GetSupplier => await GetSupplierAsync(Number(input, "supplierId"), cancellationToken),
            AssistantPrompt.QuerySalesData => await QueryAsync(conversationId, Text(input, "sql"), Text(input, "purpose"), cancellationToken),
            _ => new ToolOutcome($"There is no tool called {name}.", true, $"Unknown tool {name}"),
        };

    /// <summary>A short label for a lookup while it runs, before its result (and the customer's name) is known.</summary>
    public static string RunningLabel(string name, IReadOnlyDictionary<string, JsonElement> input) => name switch
    {
        AssistantPrompt.SearchCustomers => $"Searching customers for “{Text(input, "query")}”",
        AssistantPrompt.GetCustomer => "Looking up the customer",
        AssistantPrompt.ListInvoices => "Checking invoices",
        AssistantPrompt.GetDocument => $"Opening {Text(input, "number")}",
        AssistantPrompt.SearchProducts => $"Searching products for “{Text(input, "query")}”",
        AssistantPrompt.SearchSuppliers => $"Searching suppliers for “{Text(input, "query")}”",
        AssistantPrompt.GetSupplier => "Looking up the supplier",
        AssistantPrompt.QuerySalesData => Text(input, "purpose") is { Length: > 0 } purpose ? purpose : "Querying sales data",
        AssistantPrompt.CreateDraft => "Writing a draft",
        _ => name,
    };

    private async Task<ToolOutcome> SearchCustomersAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Length < 2)
        {
            return new ToolOutcome("Give at least two characters to search for.", true, "Searched customers");
        }

        var digits = new string([.. query.Where(char.IsDigit)]);
        var upper = query.ToUpperInvariant();
        var customers = await db.Customers.AsNoTracking()
            .Where(c => c.Name.Contains(query) || (c.TradeName != null && c.TradeName.Contains(query)) || c.Code == upper
                || c.BillingAddress.City.Contains(query) || c.Gstin == upper
                || (digits.Length >= 6 && c.Phone != null && c.Phone.Contains(digits)))
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.Name)
            .Take(10)
            .Select(c => new { c.Id, c.Code, c.Name, c.TradeName, City = c.BillingAddress.City, SalesRep = c.AssignedRep != null ? c.AssignedRep.FullName : null, c.IsActive })
            .ToListAsync(cancellationToken);

        var balances = await BalancesAsync([.. customers.Select(c => c.Id)], cancellationToken);
        var label = $"Searched customers for “{query}”";
        if (customers.Count == 0)
        {
            return new ToolOutcome(Serialize(new { matches = 0, note = "No customer matches. Try part of the name, the city or the code." }), false, label);
        }

        return new ToolOutcome(Serialize(new
        {
            matches = customers.Count,
            customers = customers.Select(c => new
            {
                id = c.Id,
                c.Code,
                c.Name,
                c.TradeName,
                c.City,
                c.SalesRep,
                active = c.IsActive,
                outstanding = balances.GetValueOrDefault(c.Id).Outstanding,
                overdue = balances.GetValueOrDefault(c.Id).Overdue,
            }),
        }), false, label);
    }

    private async Task<ToolOutcome> GetCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new
            {
                c.Id, c.Code, c.Name, c.TradeName, c.ContactPerson, c.Gstin, City = c.BillingAddress.City, StateCode = c.BillingAddress.StateCode,
                c.CreditLimit, c.PaymentTermsDays, SalesRep = c.AssignedRep != null ? c.AssignedRep.FullName : null, c.Notes, c.IsActive,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return NoCustomer(customerId);
        }

        var today = clock.Today;
        var thisYear = FinancialYear.StartYear(today);
        var state = await db.States.Where(s => s.Code == customer.StateCode).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken);
        var invoices = db.Invoices.AsNoTracking().Where(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Cancelled);
        var salesByYear = await invoices
            .Where(i => i.FinancialYear >= thisYear - 1)
            .GroupBy(i => i.FinancialYear)
            .Select(g => new { Year = g.Key, Sales = g.Sum(i => i.TaxableValue), Invoices = g.Count() })
            .ToListAsync(cancellationToken);
        // Sales are net of credit notes, each counted in its own financial year.
        var creditNotes = db.CreditNotes.AsNoTracking().Where(c => c.CustomerId == customerId && c.Status == ReturnDocumentStatus.Issued);
        var returnsByYear = await creditNotes
            .Where(c => c.FinancialYear >= thisYear - 1)
            .GroupBy(c => c.FinancialYear)
            .Select(g => new { Year = g.Key, Value = g.Sum(c => c.TaxableValue) })
            .ToDictionaryAsync(r => r.Year, r => r.Value, cancellationToken);
        var unusedCredit = await creditNotes.SumAsync(c => (decimal?)(c.GrandTotal - c.AmountAdjusted - c.AmountRefunded), cancellationToken) ?? 0m;
        var lastInvoice = await invoices.MaxAsync(i => (DateOnly?)i.InvoiceDate, cancellationToken);
        var open = await invoices.Where(i => Open.Contains(i.Status))
            .Select(i => new { Outstanding = i.GrandTotal - i.AmountPaid - i.AmountCredited, i.DueDate })
            .ToListAsync(cancellationToken);
        var overdue = open.Where(i => i.DueDate < today).ToList();

        var recentOrders = await db.SalesOrders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id)
            .Take(5)
            .Select(o => new { o.Number, o.QuotationNumber, Status = o.Status.ToString(), OrderDate = o.OrderDate, o.GrandTotal })
            .ToListAsync(cancellationToken);
        var topProducts = await (
                from l in db.InvoiceLines.AsNoTracking()
                join i in db.Invoices on l.InvoiceId equals i.Id
                where i.CustomerId == customerId && i.FinancialYear == thisYear && i.Status != InvoiceStatus.Cancelled
                select l)
            .GroupBy(l => new { l.ProductSku, l.ProductName, l.Unit })
            .Select(g => new { g.Key.ProductSku, g.Key.ProductName, g.Key.Unit, Quantity = g.Sum(l => l.Quantity), Sales = g.Sum(l => l.TaxableValue) })
            .OrderByDescending(p => p.Sales)
            .Take(5)
            .ToListAsync(cancellationToken);

        return new ToolOutcome(Serialize(new
        {
            id = customer.Id,
            customer.Code,
            customer.Name,
            customer.TradeName,
            customer.ContactPerson,
            customer.Gstin,
            customer.City,
            state,
            customer.CreditLimit,
            customer.PaymentTermsDays,
            customer.SalesRep,
            active = customer.IsActive,
            customer.Notes,
            salesThisFinancialYear = (salesByYear.FirstOrDefault(s => s.Year == thisYear)?.Sales ?? 0m) - returnsByYear.GetValueOrDefault(thisYear),
            invoicesThisFinancialYear = salesByYear.FirstOrDefault(s => s.Year == thisYear)?.Invoices ?? 0,
            returnsThisFinancialYear = returnsByYear.GetValueOrDefault(thisYear),
            salesLastFinancialYear = (salesByYear.FirstOrDefault(s => s.Year == thisYear - 1)?.Sales ?? 0m) - returnsByYear.GetValueOrDefault(thisYear - 1),
            lastInvoiceDate = lastInvoice,
            outstanding = open.Sum(i => i.Outstanding),
            openInvoices = open.Count,
            overdue = overdue.Sum(i => i.Outstanding),
            overdueInvoices = overdue.Count,
            unusedCredit,
            recentOrders = recentOrders.Select(o => new { number = o.QuotationNumber ?? o.Number, o.Status, date = o.OrderDate, o.GrandTotal }),
            topProductsThisFinancialYear = topProducts.Select(p => new { sku = p.ProductSku, name = p.ProductName, unit = p.Unit.ToString(), p.Quantity, p.Sales }),
        }), false, $"Looked up {customer.Name}");
    }

    private async Task<ToolOutcome> ListInvoicesAsync(int customerId, string status, CancellationToken cancellationToken)
    {
        string? customerName = null;
        if (customerId > 0)
        {
            customerName = await db.Customers.Where(c => c.Id == customerId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken);
            if (customerName is null)
            {
                return NoCustomer(customerId);
            }
        }

        var today = clock.Today;
        var query = db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled);
        if (customerId > 0)
        {
            query = query.Where(i => i.CustomerId == customerId);
        }

        status = status.ToLowerInvariant() switch { "overdue" => "overdue", "all" => "all", _ => "outstanding" };
        if (status != "all")
        {
            query = query.Where(i => Open.Contains(i.Status));
        }

        if (status == "overdue")
        {
            query = query.Where(i => i.DueDate < today);
        }

        var count = await query.CountAsync(cancellationToken);
        var totalAmount = await query.SumAsync(i => i.GrandTotal, cancellationToken);
        var totalOutstanding = await query.SumAsync(i => i.GrandTotal - i.AmountPaid - i.AmountCredited, cancellationToken);
        var ordered = status == "all"
            ? query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Id)
            : query.OrderBy(i => i.DueDate).ThenBy(i => i.Id);
        var invoices = await ordered
            .Take(50)
            .Select(i => new { i.Number, i.CustomerId, i.CustomerName, i.InvoiceDate, i.DueDate, i.GrandTotal, i.AmountPaid, i.AmountCredited, i.Status })
            .ToListAsync(cancellationToken);

        var which = status switch { "overdue" => "overdue invoices", "all" => "invoices", _ => "outstanding invoices" };
        return new ToolOutcome(Serialize(new
        {
            customer = customerName,
            status,
            count,
            totalAmount,
            totalOutstanding,
            shown = invoices.Count,
            invoices = invoices.Select(i => new
            {
                number = i.Number,
                customerId = customerId > 0 ? (int?)null : i.CustomerId,
                customer = customerId > 0 ? null : i.CustomerName,
                date = i.InvoiceDate,
                dueDate = i.DueDate,
                total = i.GrandTotal,
                paid = i.AmountPaid,
                credited = i.AmountCredited,
                outstanding = i.GrandTotal - i.AmountPaid - i.AmountCredited,
                daysOverdue = Open.Contains(i.Status) && i.DueDate < today ? today.DayNumber - i.DueDate.DayNumber : 0,
                status = i.Status.ToString(),
            }),
        }), false, customerName is null ? $"Listed {which}" : $"Listed {which} for {customerName}");
    }

    private async Task<ToolOutcome> GetDocumentAsync(string number, CancellationToken cancellationToken)
    {
        number = number.Trim().ToUpperInvariant();
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines).Include(i => i.Payments)
            .SingleOrDefaultAsync(i => i.Number == number, cancellationToken);
        if (invoice is not null)
        {
            if (!currentUser.Has(Permissions.InvoicesView))
            {
                return NoAccess("invoices");
            }

            var today = clock.Today;
            var credits = await db.CreditNotes.AsNoTracking()
                .Where(c => c.InvoiceId == invoice.Id && c.Status == ReturnDocumentStatus.Issued)
                .OrderBy(c => c.CreditNoteDate)
                .Select(c => new { number = c.Number, date = c.CreditNoteDate, kind = c.Kind.ToString(), reason = c.Reason.ToString(), total = c.GrandTotal })
                .ToListAsync(cancellationToken);
            return new ToolOutcome(Serialize(new
            {
                type = "invoice",
                invoice.Number,
                customerId = invoice.CustomerId,
                customer = invoice.CustomerName,
                date = invoice.InvoiceDate,
                invoice.DueDate,
                status = invoice.Status.ToString(),
                placeOfSupply = invoice.PlaceOfSupplyStateName,
                invoice.CustomerReference,
                lines = invoice.Lines.OrderBy(l => l.LineNumber).Select(l => new
                {
                    sku = l.ProductSku, product = l.ProductName, l.Quantity, unit = l.Unit.ToString(), l.UnitPrice, l.DiscountPercent, l.TaxableValue, l.GstRate, l.LineTotal,
                }),
                invoice.TaxableValue,
                gst = invoice.CgstAmount + invoice.SgstAmount + invoice.IgstAmount,
                invoice.RoundOff,
                invoice.GrandTotal,
                invoice.AmountPaid,
                credited = invoice.AmountCredited,
                outstanding = invoice.Status == InvoiceStatus.Cancelled ? 0 : invoice.GrandTotal - invoice.AmountPaid - invoice.AmountCredited,
                daysOverdue = Open.Contains(invoice.Status) && invoice.DueDate < today ? today.DayNumber - invoice.DueDate.DayNumber : 0,
                payments = invoice.Payments.OrderBy(p => p.PaymentDate).Select(p => new { date = p.PaymentDate, p.Amount, mode = p.Mode.ToString(), p.Reference }),
                creditNotes = credits,
            }), false, $"Opened invoice {invoice.Number}");
        }

        if (await GetCreditNoteAsync(number, cancellationToken) is { } creditNote)
        {
            return creditNote;
        }

        if (await GetPurchaseDocumentAsync(number, cancellationToken) is { } purchase)
        {
            return purchase;
        }

        var order = await db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer).Include(o => o.Lines).ThenInclude(l => l.Product)
            .SingleOrDefaultAsync(o => o.Number == number || o.QuotationNumber == number, cancellationToken);
        if (order is null)
        {
            return new ToolOutcome($"No invoice, credit note, order, quotation, purchase order, supplier bill or supplier return is numbered {number}.", true, $"Looked for {number}");
        }

        if (!currentUser.Has(Permissions.OrdersView))
        {
            return NoAccess("orders and quotations");
        }

        var invoiceNumber = order.InvoiceId is { } invoiceId
            ? await db.Invoices.Where(i => i.Id == invoiceId).Select(i => i.Number).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new ToolOutcome(Serialize(new
        {
            type = order.QuotationNumber == number ? "quotation" : "order",
            orderNumber = order.Number,
            order.QuotationNumber,
            invoiceNumber,
            customerId = order.CustomerId,
            customer = order.Customer?.Name,
            date = order.OrderDate,
            order.ValidUntil,
            status = order.Status.ToString(),
            order.CustomerReference,
            order.Notes,
            lines = order.Lines.OrderBy(l => l.LineNumber).Select(l => new
            {
                sku = l.Product?.Sku, product = l.Product?.Name, l.Quantity, unit = l.Product?.Unit.ToString(), l.UnitPrice, l.DiscountPercent, l.GstRate,
            }),
            order.TaxableValue,
            gst = order.TaxAmount,
            order.GrandTotal,
        }), false, $"Opened {(order.QuotationNumber == number ? "quotation" : "order")} {number}");
    }

    private async Task<ToolOutcome> SearchProductsAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Length < 2)
        {
            return new ToolOutcome("Give at least two characters to search for.", true, "Searched products");
        }

        var alias = AliasText.Normalize(query);
        var aliasIds = alias.Length == 0
            ? []
            : await db.ProductAliases.Where(a => a.NormalizedAlias.Contains(alias)).Select(a => a.ProductId).ToListAsync(cancellationToken);
        var today = clock.Today;
        var products = await db.Products.AsNoTracking()
            .Where(p => p.Name.Contains(query) || p.Sku.Contains(query) || p.Category!.Name.Contains(query) || aliasIds.Contains(p.Id))
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
            .Take(15)
            .Select(p => new
            {
                id = p.Id, p.Sku, p.Name, category = p.Category!.Name, p.Unit, p.ListPrice, gstRate = p.HsnCode!.GstRate,
                onHand = p.Stock != null ? p.Stock.QuantityOnHand : 0m, p.ReorderLevel, p.LeadTimeDays, active = p.IsActive,
                rate = db.ProductRates
                    .Where(r => r.ProductId == p.Id && r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today))
                    .Select(r => (decimal?)r.Rate)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new ToolOutcome(Serialize(new
        {
            matches = products.Count,
            products = products.Select(p => new
            {
                p.id, p.Sku, p.Name, p.category, unit = p.Unit.ToString(), p.ListPrice,
                // What an order placed today is priced at: this period's rate when one is set, else the list price.
                priceToday = p.rate ?? p.ListPrice, p.gstRate, p.onHand, p.ReorderLevel, p.LeadTimeDays, p.active,
                stockStatus = p.onHand <= 0 ? "OutOfStock" : p.onHand <= p.ReorderLevel ? "Low" : "InStock",
            }),
        }), false, $"Searched products for “{query}”");
    }

    private async Task<ToolOutcome> QueryAsync(Guid conversationId, string sql, string purpose, CancellationToken cancellationToken)
    {
        var label = purpose.Length > 0 ? purpose : "Queried sales data";
        var outcome = await queries.RunAsync(conversationId, $"Assistant: {purpose}", sql, cancellationToken);
        return outcome switch
        {
            { Outcome: AiQueryOutcome.Rejected } => new ToolOutcome($"Refused by the SQL checker: {outcome.Problem}", true, label, sql),
            { Result: { } result } => new ToolOutcome(QueryResultJson.ForClaude(null, result), false, label, sql),
            _ => new ToolOutcome($"The database returned an error: {outcome.Problem}", true, label, sql),
        };
    }

    private async Task<ToolOutcome> SearchSuppliersAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Length < 2)
        {
            return new ToolOutcome("Give at least two characters to search for.", true, "Searched suppliers");
        }

        var today = clock.Today;
        var upper = query.ToUpperInvariant();
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => s.Name.Contains(query) || (s.TradeName != null && s.TradeName.Contains(query)) || s.Code == upper
                || s.Address.City.Contains(query) || s.Gstin == upper)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.Name)
            .Take(10)
            .Select(s => new
            {
                id = s.Id, s.Code, s.Name, s.TradeName, city = s.Address.City, active = s.IsActive,
                openOrders = db.PurchaseOrders.Count(o => o.SupplierId == s.Id && OpenOrders.Contains(o.Status)),
                lateOrders = db.PurchaseOrders.Count(o => o.SupplierId == s.Id && Receivable.Contains(o.Status) && o.ExpectedDate < today),
                payable = db.SupplierBills.Where(b => b.SupplierId == s.Id && OpenBills.Contains(b.Status)).Sum(b => (decimal?)(b.GrandTotal - b.AmountPaid - b.AmountCredited)) ?? 0m,
                overdue = db.SupplierBills.Where(b => b.SupplierId == s.Id && OpenBills.Contains(b.Status) && b.DueDate < today).Sum(b => (decimal?)(b.GrandTotal - b.AmountPaid - b.AmountCredited)) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        var label = $"Searched suppliers for “{query}”";
        return suppliers.Count == 0
            ? new ToolOutcome(Serialize(new { matches = 0, note = "No supplier matches. Try part of the name, the city or the code." }), false, label)
            : new ToolOutcome(Serialize(new { matches = suppliers.Count, suppliers }), false, label);
    }

    private async Task<ToolOutcome> GetSupplierAsync(int supplierId, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == supplierId)
            .Select(s => new { s.Id, s.Code, s.Name, s.TradeName, s.ContactPerson, s.Gstin, City = s.Address.City, StateCode = s.Address.StateCode, s.PaymentTermsDays, s.Notes, s.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (supplier is null)
        {
            return new ToolOutcome($"No supplier has id {supplierId}. Use {AssistantPrompt.SearchSuppliers} to find the right id.", true, "Looked up a supplier");
        }

        var today = clock.Today;
        var thisYear = FinancialYear.StartYear(today);
        var state = await db.States.Where(s => s.Code == supplier.StateCode).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken);

        object? orders = null;
        if (currentUser.Has(Permissions.PurchaseOrdersView))
        {
            var open = await db.PurchaseOrders.AsNoTracking()
                .Where(o => o.SupplierId == supplierId && OpenOrders.Contains(o.Status))
                .OrderBy(o => o.ExpectedDate ?? o.OrderDate)
                .Take(20)
                .Select(o => new
                {
                    o.Number, Status = o.Status.ToString(), o.OrderDate, o.ExpectedDate, o.GrandTotal,
                    Lines = o.Lines.Where(l => l.QuantityReceived < l.Quantity).OrderBy(l => l.LineNumber)
                        .Select(l => new { sku = l.Product!.Sku, product = l.Product.Name, unit = l.Product.Unit.ToString(), ordered = l.Quantity, received = l.QuantityReceived })
                        .ToList(),
                })
                .ToListAsync(cancellationToken);
            orders = open.Select(o => new
            {
                number = o.Number,
                status = o.Status,
                orderDate = o.OrderDate,
                expectedDate = o.ExpectedDate,
                daysLate = o.Status != nameof(PurchaseOrderStatus.PendingApproval) && o.ExpectedDate is { } expected && expected < today ? today.DayNumber - expected.DayNumber : 0,
                total = o.GrandTotal,
                stillToCome = o.Lines.Select(l => new { l.sku, l.product, l.unit, l.ordered, l.received, pending = l.ordered - l.received }),
            });
        }

        object? bills = null;
        decimal? payable = null, overdue = null, purchasesThisYear = null, unusedCredit = null;
        DateOnly? lastBill = null;
        if (currentUser.Has(Permissions.SupplierBillsView))
        {
            var all = db.SupplierBills.AsNoTracking().Where(b => b.SupplierId == supplierId && b.Status != SupplierBillStatus.Cancelled);
            var returns = db.SupplierReturns.AsNoTracking().Where(r => r.SupplierId == supplierId && r.Status == ReturnDocumentStatus.Issued);
            purchasesThisYear = (await all.Where(b => b.FinancialYear == thisYear).SumAsync(b => (decimal?)(b.TaxableValue + b.ChargesAmount), cancellationToken) ?? 0m)
                - (await returns.Where(r => r.FinancialYear == thisYear).SumAsync(r => (decimal?)r.TaxableValue, cancellationToken) ?? 0m);
            unusedCredit = await returns.SumAsync(r => (decimal?)(r.GrandTotal - r.AmountAdjusted - r.AmountRefunded), cancellationToken) ?? 0m;
            lastBill = await all.MaxAsync(b => (DateOnly?)b.BillDate, cancellationToken);
            var open = all.Where(b => OpenBills.Contains(b.Status));
            // Totals over every unpaid bill, not just the ones listed.
            payable = await open.SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid - b.AmountCredited), cancellationToken) ?? 0m;
            overdue = await open.Where(b => b.DueDate < today).SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid - b.AmountCredited), cancellationToken) ?? 0m;
            var unpaid = await open
                .OrderBy(b => b.DueDate)
                .Take(30)
                .Select(b => new { b.Number, b.SupplierInvoiceNumber, b.BillDate, b.DueDate, b.GrandTotal, b.AmountPaid, b.AmountCredited, Match = b.MatchStatus.ToString() })
                .ToListAsync(cancellationToken);
            bills = unpaid.Select(b => new
            {
                number = b.Number,
                supplierInvoice = b.SupplierInvoiceNumber,
                date = b.BillDate,
                dueDate = b.DueDate,
                total = b.GrandTotal,
                outstanding = b.GrandTotal - b.AmountPaid - b.AmountCredited,
                daysOverdue = b.DueDate < today ? today.DayNumber - b.DueDate.DayNumber : 0,
                match = b.Match,
            });
        }

        return new ToolOutcome(Serialize(new
        {
            id = supplier.Id,
            supplier.Code,
            supplier.Name,
            supplier.TradeName,
            supplier.ContactPerson,
            supplier.Gstin,
            supplier.City,
            state,
            supplier.PaymentTermsDays,
            active = supplier.IsActive,
            supplier.Notes,
            purchasesThisFinancialYear = purchasesThisYear,
            lastBillDate = lastBill,
            payable,
            overdue,
            unusedCredit,
            openPurchaseOrders = orders ?? "No permission to see purchase orders.",
            unpaidBills = bills ?? "No permission to see supplier bills.",
        }), false, $"Looked up {supplier.Name}");
    }

    /// <summary>A purchase order or supplier bill by number (ours, or the supplier's invoice number), or null.</summary>
    private async Task<ToolOutcome?> GetPurchaseDocumentAsync(string number, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking()
            .Include(o => o.Supplier).Include(o => o.Lines).ThenInclude(l => l.Product)
            .SingleOrDefaultAsync(o => o.Number == number, cancellationToken);
        if (order is not null)
        {
            if (!currentUser.Has(Permissions.PurchaseOrdersView))
            {
                return NoAccess("purchase orders");
            }

            var today = clock.Today;
            return new ToolOutcome(Serialize(new
            {
                type = "purchase order",
                order.Number,
                supplierId = order.SupplierId,
                supplier = order.Supplier?.Name,
                date = order.OrderDate,
                order.ExpectedDate,
                status = order.Status.ToString(),
                daysLate = Receivable.Contains(order.Status) && order.ExpectedDate is { } expected && expected < today ? today.DayNumber - expected.DayNumber : 0,
                order.SupplierReference,
                order.Notes,
                lines = order.Lines.OrderBy(l => l.LineNumber).Select(l => new
                {
                    sku = l.Product?.Sku, product = l.Product?.Name, unit = l.Product?.Unit.ToString(), ordered = l.Quantity, l.UnitPrice, l.GstRate,
                    received = l.QuantityReceived, billed = l.QuantityBilled, stillToCome = Math.Max(0, l.Quantity - l.QuantityReceived),
                }),
                order.TaxableValue,
                gst = order.TaxAmount,
                order.GrandTotal,
            }), false, $"Opened purchase order {order.Number}");
        }

        var bill = await db.SupplierBills.AsNoTracking()
            .Include(b => b.Lines).Include(b => b.Payments).Include(b => b.PurchaseOrder)
            .Where(b => b.Number == number || b.SupplierInvoiceNumber == number)
            .OrderByDescending(b => b.Number == number)
            .FirstOrDefaultAsync(cancellationToken);
        if (bill is null)
        {
            return null;
        }

        if (!currentUser.Has(Permissions.SupplierBillsView))
        {
            return NoAccess("supplier bills");
        }

        var day = clock.Today;
        var billReturns = await db.SupplierReturns.AsNoTracking()
            .Where(r => r.SupplierBillId == bill.Id && r.Status == ReturnDocumentStatus.Issued)
            .OrderBy(r => r.ReturnDate)
            .Select(r => new { number = r.Number, date = r.ReturnDate, reason = r.Reason.ToString(), total = r.GrandTotal, supplierCreditNote = r.SupplierCreditNoteNumber })
            .ToListAsync(cancellationToken);
        return new ToolOutcome(Serialize(new
        {
            type = "supplier bill",
            bill.Number,
            supplierInvoice = bill.SupplierInvoiceNumber,
            supplierId = bill.SupplierId,
            supplier = bill.SupplierName,
            purchaseOrder = bill.PurchaseOrder?.Number,
            date = bill.BillDate,
            bill.DueDate,
            status = bill.Status.ToString(),
            match = bill.MatchStatus.ToString(),
            lines = bill.Lines.OrderBy(l => l.LineNumber).Select(l => new
            {
                sku = l.ProductSku, product = l.ProductName, l.Quantity, unit = l.Unit.ToString(), l.UnitPrice, orderedPrice = l.OrderedPrice, l.TaxableValue, l.GstRate, l.LineTotal, issue = l.Issue,
            }),
            bill.TaxableValue,
            charges = bill.ChargesAmount,
            gst = bill.CgstAmount + bill.SgstAmount + bill.IgstAmount,
            bill.GrandTotal,
            bill.AmountPaid,
            credited = bill.AmountCredited,
            outstanding = bill.Status == SupplierBillStatus.Cancelled ? 0 : bill.GrandTotal - bill.AmountPaid - bill.AmountCredited,
            daysOverdue = OpenBills.Contains(bill.Status) && bill.DueDate < day ? day.DayNumber - bill.DueDate.DayNumber : 0,
            payments = bill.Payments.OrderBy(p => p.PaymentDate).Select(p => new { date = p.PaymentDate, p.Amount, mode = p.Mode.ToString(), p.Reference }),
            returns = billReturns,
        }), false, $"Opened supplier bill {bill.Number}");
    }

    /// <summary>A credit note or supplier return (debit note) by number, or null.</summary>
    private async Task<ToolOutcome?> GetCreditNoteAsync(string number, CancellationToken cancellationToken)
    {
        var note = await db.CreditNotes.AsNoTracking()
            .Include(c => c.Lines).Include(c => c.Invoice)
            .SingleOrDefaultAsync(c => c.Number == number, cancellationToken);
        if (note is not null)
        {
            if (!currentUser.Has(Permissions.CreditNotesView))
            {
                return NoAccess("credit notes");
            }

            return new ToolOutcome(Serialize(new
            {
                type = "credit note",
                note.Number,
                invoice = note.Invoice?.Number,
                customerId = note.CustomerId,
                customer = note.CustomerName,
                date = note.CreditNoteDate,
                status = note.Status.ToString(),
                kind = note.Kind.ToString(),
                reason = note.Reason.ToString(),
                details = note.ReasonNote,
                reducesGst = note.WithGst,
                lines = note.Lines.OrderBy(l => l.LineNumber).Select(l => new
                {
                    sku = l.ProductSku, product = l.ProductName, quantity = l.Quantity, unit = l.Unit.ToString(), returnedTo = l.Disposition?.ToString(), l.TaxableValue, l.LineTotal,
                }),
                note.TaxableValue,
                gst = note.CgstAmount + note.SgstAmount + note.IgstAmount,
                note.GrandTotal,
                setAgainstInvoices = note.AmountAdjusted,
                refunded = note.AmountRefunded,
                creditLeft = note.Status == ReturnDocumentStatus.Issued ? note.GrandTotal - note.AmountAdjusted - note.AmountRefunded : 0m,
            }), false, $"Opened credit note {note.Number}");
        }

        var ret = await db.SupplierReturns.AsNoTracking()
            .Include(r => r.Lines).Include(r => r.SupplierBill)
            .SingleOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (ret is null)
        {
            return null;
        }

        if (!currentUser.Has(Permissions.SupplierReturnsView))
        {
            return NoAccess("supplier returns");
        }

        return new ToolOutcome(Serialize(new
        {
            type = "supplier return (debit note)",
            ret.Number,
            bill = ret.SupplierBill?.Number,
            supplierInvoice = ret.SupplierBill?.SupplierInvoiceNumber,
            supplierId = ret.SupplierId,
            supplier = ret.SupplierName,
            date = ret.ReturnDate,
            status = ret.Status.ToString(),
            reason = ret.Reason.ToString(),
            details = ret.ReasonNote,
            supplierCreditNote = ret.SupplierCreditNoteNumber,
            lines = ret.Lines.OrderBy(l => l.LineNumber).Select(l => new { sku = l.ProductSku, product = l.ProductName, l.Quantity, unit = l.Unit.ToString(), l.UnitPrice, l.TaxableValue, l.LineTotal }),
            ret.TaxableValue,
            gst = ret.CgstAmount + ret.SgstAmount + ret.IgstAmount,
            ret.GrandTotal,
            setAgainstBills = ret.AmountAdjusted,
            refunded = ret.AmountRefunded,
            creditLeft = ret.Status == ReturnDocumentStatus.Issued ? ret.GrandTotal - ret.AmountAdjusted - ret.AmountRefunded : 0m,
        }), false, $"Opened supplier return {ret.Number}");
    }

    /// <summary>Outstanding and overdue amounts for each customer.</summary>
    private async Task<Dictionary<int, (decimal Outstanding, decimal Overdue)>> BalancesAsync(int[] customerIds, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var rows = await db.Invoices.AsNoTracking()
            .Where(i => customerIds.Contains(i.CustomerId) && Open.Contains(i.Status))
            .GroupBy(i => i.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Outstanding = g.Sum(i => i.GrandTotal - i.AmountPaid - i.AmountCredited),
                Overdue = g.Where(i => i.DueDate < today).Sum(i => (decimal?)(i.GrandTotal - i.AmountPaid - i.AmountCredited)) ?? 0m,
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.CustomerId, r => (r.Outstanding, r.Overdue));
    }

    private static ToolOutcome NoCustomer(int id) =>
        new($"No customer has id {id}. Use {AssistantPrompt.SearchCustomers} to find the right id.", true, "Looked up a customer");

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    internal static string Text(IReadOnlyDictionary<string, JsonElement> input, string name) =>
        input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";

    internal static int Number(IReadOnlyDictionary<string, JsonElement> input, string name) =>
        input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
        : input.TryGetValue(name, out value) && value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number
        : 0;
}
