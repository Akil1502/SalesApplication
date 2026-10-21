using SalesApp.Core.Contracts.Users;
using SalesApp.Core.Localization;

namespace SalesApp.Core.Security;

/// <summary>
/// What a user may do, as permission keys. A role is a set of these; a user gets their role's set plus or minus any
/// changes made for that user alone. The API checks them on every request (see RequirePermissionAttribute), and the
/// web app uses them to show menus and buttons. The keys are stored in the database, so never rename one.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "dashboard.view";
    public const string OrdersView = "orders.view";
    public const string OrdersEdit = "orders.edit";
    public const string SmartEntryUse = "smart-entry.use";
    public const string InvoicesView = "invoices.view";
    public const string InvoicesEdit = "invoices.edit";
    public const string PaymentsView = "payments.view";
    public const string PaymentsEdit = "payments.edit";
    public const string CreditNotesView = "credit-notes.view";
    public const string CreditNotesEdit = "credit-notes.edit";
    public const string PurchaseOrdersView = "purchase-orders.view";
    public const string PurchaseOrdersEdit = "purchase-orders.edit";
    public const string SupplierBillsView = "supplier-bills.view";
    public const string SupplierBillsEdit = "supplier-bills.edit";
    public const string SupplierPaymentsView = "supplier-payments.view";
    public const string SupplierPaymentsEdit = "supplier-payments.edit";
    public const string SupplierReturnsView = "supplier-returns.view";
    public const string SupplierReturnsEdit = "supplier-returns.edit";
    public const string CustomersView = "customers.view";
    public const string CustomersEdit = "customers.edit";
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersEdit = "suppliers.edit";
    public const string ProductsView = "products.view";
    public const string ProductsEdit = "products.edit";
    public const string RatesView = "rates.view";
    public const string RatesEdit = "rates.edit";
    public const string CatalogView = "catalog.view";
    public const string CatalogEdit = "catalog.edit";
    public const string StockView = "stock.view";
    public const string StockEdit = "stock.edit";
    public const string ReportsView = "reports.view";
    public const string AskDataUse = "ask-data.use";
    public const string InsightsView = "insights.view";
    public const string AssistantUse = "assistant.use";
    public const string UsersView = "users.view";
    public const string UsersEdit = "users.edit";
    public const string SettingsEdit = "settings.edit";
    public const string AiUsageView = "ai-usage.view";
    public const string AuditView = "audit.view";

    // Special rights: not menus, but rules inside them.
    public const string CostPricesView = "cost-prices.view";
    public const string CreditTermsEdit = "credit-terms.edit";
    public const string CreditLimitOverride = "credit-limit.override";
    public const string SalesRepAssignable = "sales-rep.assignable";
    public const string PurchaseOrdersApprove = "purchase-orders.approve";
    public const string SupplierBillsApprove = "supplier-bills.approve";
    public const string BillReadingUse = "bill-reading.use";

    /// <summary>
    /// Notifications about every customer's orders, payments and overdue invoices. Without it, people hear only about
    /// the customers they look after (their assigned sales rep).
    /// </summary>
    public const string AllCustomersActivity = "customers.all-activity";

    /// <summary>The menus, in sidebar order: a View permission, and an Add &amp; change one where the menu changes data.</summary>
    public static readonly IReadOnlyList<PermissionMenuDto> Menus =
    [
        new(L.Msg("Overview"), L.Msg("Dashboard"), DashboardView, L.Msg("Sales, receivables and stock at a glance."), null, null),
        new(L.Msg("Sales"), L.Msg("Orders & quotations"), OrdersView, L.Msg("See and print orders and quotations."), OrdersEdit, L.Msg("Create, change, quote, confirm and cancel them.")),
        new(L.Msg("Sales"), L.Msg("Smart order entry"), SmartEntryUse, L.Msg("Read orders from email, WhatsApp, a PDF or a photo with AI. Needs Orders: Add & change."), null, null),
        new(L.Msg("Sales"), L.Msg("Invoices"), InvoicesView, L.Msg("See and print invoices."), InvoicesEdit, L.Msg("Raise invoices from confirmed orders, and cancel them. Needs Orders: View.")),
        new(L.Msg("Sales"), L.Msg("Payments"), PaymentsView, L.Msg("See payments received."), PaymentsEdit, L.Msg("Record payments, from the invoice. Needs Invoices: View.")),
        new(L.Msg("Sales"), L.Msg("Credit notes"), CreditNotesView, L.Msg("See and print credit notes, and the credit customers have with us."), CreditNotesEdit,
            L.Msg("Issue credit notes from invoices for returns and price corrections, set credit against invoices, record refunds and cancel credit notes. Needs Invoices: View.")),
        new(L.Msg("Purchases"), L.Msg("Purchase orders"), PurchaseOrdersView, L.Msg("See and print purchase orders and what has arrived against them."), PurchaseOrdersEdit, L.Msg("Create and change orders, place them with suppliers, close and cancel them. Needs Suppliers and Products: View.")),
        new(L.Msg("Purchases"), L.Msg("Supplier bills"), SupplierBillsView, L.Msg("See supplier bills, what we owe and the price and quantity checks."), SupplierBillsEdit, L.Msg("Enter bills against purchase orders, and cancel them. Needs Purchase orders: View.")),
        new(L.Msg("Purchases"), L.Msg("Supplier payments"), SupplierPaymentsView, L.Msg("See payments made to suppliers."), SupplierPaymentsEdit, L.Msg("Record payments to suppliers, from the bill. Needs Supplier bills: View.")),
        new(L.Msg("Purchases"), L.Msg("Supplier returns"), SupplierReturnsView, L.Msg("See goods returned to suppliers (debit notes) and the credit we have with them."), SupplierReturnsEdit,
            L.Msg("Return goods against supplier bills, set credit against their bills, record refunds and cancel returns. Needs Supplier bills: View.")),
        new(L.Msg("Masters"), L.Msg("Customers"), CustomersView, L.Msg("See customers and what they owe."), CustomersEdit, L.Msg("Add and change customers. Credit terms need their own right.")),
        new(L.Msg("Masters"), L.Msg("Suppliers"), SuppliersView, L.Msg("See suppliers and what we owe them."), SuppliersEdit, L.Msg("Add and change suppliers.")),
        new(L.Msg("Masters"), L.Msg("Products"), ProductsView, L.Msg("See products and selling prices."), ProductsEdit, L.Msg("Add and change products.")),
        new(L.Msg("Masters"), L.Msg("Rate updates"), RatesView, L.Msg("See each product's rate for each period."), RatesEdit, L.Msg("Set rates for a period, including from Excel, and change or remove them.")),
        new(L.Msg("Masters"), L.Msg("Categories & HSN"), CatalogView, L.Msg("See product categories and HSN codes."), CatalogEdit, L.Msg("Add and change them.")),
        new(L.Msg("Inventory"), L.Msg("Stock"), StockView, L.Msg("See stock levels, movements and goods receipts."), StockEdit, L.Msg("Receive goods against purchase orders, and make stock adjustments. Needs Purchase orders: View.")),
        new(L.Msg("Insights"), L.Msg("Reports"), ReportsView, L.Msg("Run and export the standard reports."), null, null),
        new(L.Msg("Insights"), L.Msg("Ask your data"), AskDataUse, L.Msg("Ask questions in plain English or Tamil. Answers can draw on all sales, customer, product, stock and payment figures, and on purchases for those who can see cost prices."), null, null),
        new(L.Msg("Insights"), L.Msg("AI insights"), InsightsView, L.Msg("See the AI summary, forecast and alerts, and refresh them."), null, null),
        new(L.Msg("Insights"), L.Msg("Assistant"), AssistantUse, L.Msg("Chat with the assistant. It only looks up what this user may see."), null, null),
        new(L.Msg("Administration"), L.Msg("Users & roles"), UsersView, L.Msg("See users and roles."), UsersEdit, L.Msg("Add and change users and roles, and reset passwords.")),
        new(L.Msg("Administration"), L.Msg("Company settings"), SettingsEdit, L.Msg("Change the company's details, GSTIN and bank details."), null, null),
        new(L.Msg("Administration"), L.Msg("AI usage"), AiUsageView, L.Msg("See AI spend, every call to Claude and every query the AI wrote."), null, null),
        new(L.Msg("Administration"), L.Msg("Audit log"), AuditView, L.Msg("See who changed what and when, with old and new values, plus sign-ins and downloads; and each record's history."), null, null),
    ];

    public static readonly IReadOnlyList<PermissionRightDto> SpecialRights =
    [
        new(CostPricesView, L.Msg("See cost prices and margins"), L.Msg("Cost prices, stock value and gross margin, in screens, reports, Ask your data and the assistant.")),
        new(CreditTermsEdit, L.Msg("Set credit terms"), L.Msg("Change a customer's credit limit, payment terms and sales rep.")),
        new(CreditLimitOverride, L.Msg("Override credit limits"), L.Msg("Confirm an order that takes a customer over their credit limit.")),
        new(SalesRepAssignable, L.Msg("Can be a sales rep"), L.Msg("Can be assigned customers as their sales rep.")),
        new(PurchaseOrdersApprove, L.Msg("Approve purchase orders"), L.Msg("Approve orders above the approval limit in Company settings, and place them without waiting for approval.")),
        new(SupplierBillsApprove, L.Msg("Approve bill differences"), L.Msg("Accept a supplier bill whose prices, quantities or totals don't match the order, so it can be paid.")),
        new(BillReadingUse, L.Msg("Read supplier bills with AI"), L.Msg("Fill in a supplier bill from a PDF or photo of their invoice. Uses AI credit.")),
        new(AllCustomersActivity, L.Msg("Hear about every customer"), L.Msg("Notifications about every customer's orders, payments and overdue invoices, not just the customers they look after.")),
    ];

    public static readonly IReadOnlySet<string> All = Menus
        .SelectMany(m => m.Edit is null ? [m.View] : new[] { m.View, m.Edit })
        .Concat(SpecialRights.Select(r => r.Key))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What each permission needs to be useful; granting it grants these too. Add &amp; change needs View; the rest
    /// follow where the action lives in the app (an invoice is raised from its order, a payment from its invoice).
    /// </summary>
    private static readonly Dictionary<string, string[]> Needs = Menus
        .Where(m => m.Edit is not null)
        .Select(m => (Key: m.Edit!, Needed: new[] { m.View }))
        .Concat(
        [
            (SmartEntryUse, [OrdersEdit]),
            (InvoicesEdit, [OrdersView]),
            (PaymentsEdit, [InvoicesView]),
            (CreditNotesEdit, [InvoicesView]),
            (CreditTermsEdit, [CustomersEdit]),
            (CreditLimitOverride, [OrdersEdit]),
            (PurchaseOrdersEdit, [SuppliersView, ProductsView]),
            (SupplierBillsEdit, [PurchaseOrdersView]),
            (SupplierPaymentsEdit, [SupplierBillsView]),
            (SupplierReturnsEdit, [SupplierBillsView]),
            (StockEdit, [PurchaseOrdersView]),
            (PurchaseOrdersApprove, [PurchaseOrdersView]),
            (SupplierBillsApprove, [SupplierBillsView]),
            (BillReadingUse, [SupplierBillsEdit]),
        ])
        .GroupBy(n => n.Key, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.SelectMany(n => n.Needed).Distinct().ToArray(), StringComparer.Ordinal);

    /// <summary>Each permission's needs, for the permission grid to tick and untick together with the server's rules.</summary>
    public static IReadOnlyDictionary<string, string[]> NeedsByPermission => Needs;

    /// <summary>The keys plus everything they need (Add &amp; change needs View, and so on). Unknown keys are dropped.</summary>
    public static HashSet<string> WithNeeds(IEnumerable<string> keys)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(keys.Where(All.Contains));
        while (pending.TryPop(out var key))
        {
            if (result.Add(key) && Needs.TryGetValue(key, out var needed))
            {
                foreach (var n in needed)
                {
                    pending.Push(n);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The keys without any whose needs are missing (Add &amp; change without View, and so on). Used after taking
    /// permissions away from a user, so taking away View also takes away Add &amp; change.
    /// </summary>
    public static HashSet<string> WithoutUnmetNeeds(IEnumerable<string> keys)
    {
        var result = keys.Where(All.Contains).ToHashSet(StringComparer.Ordinal);
        while (result.RemoveWhere(key => Needs.TryGetValue(key, out var needed) && !needed.All(result.Contains)) > 0)
        {
        }

        return result;
    }

    /// <summary>
    /// A user's permissions: their role's, plus the ones granted to them (with what those need), minus the ones taken
    /// away (with what depended on those).
    /// </summary>
    public static HashSet<string> Effective(IEnumerable<string> role, IEnumerable<string> granted, IEnumerable<string> removed)
    {
        var result = WithNeeds(role.Concat(granted));
        result.ExceptWith(removed);
        return WithoutUnmetNeeds(result);
    }

    /// <summary>
    /// What the built-in Sales manager role starts with: everything except administration, rate updates and the two
    /// purchase approvals, which stay with administrators unless given out.
    /// </summary>
    public static readonly IReadOnlySet<string> SalesManagerDefaults =
        All.Except([UsersView, UsersEdit, SettingsEdit, AiUsageView, AuditView, RatesView, RatesEdit, PurchaseOrdersApprove, SupplierBillsApprove])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>What the built-in Sales representative role starts with: day-to-day order and customer work.</summary>
    public static readonly IReadOnlySet<string> SalesRepDefaults = WithNeeds(
    [
        DashboardView, OrdersEdit, SmartEntryUse, InvoicesView, PaymentsView, CreditNotesView, CustomersEdit, ProductsView, StockView,
        ReportsView, AskDataUse, AssistantUse, SalesRepAssignable,
    ]);
}
