using SalesApp.Core.Contracts.Users;

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
        new("Overview", "Dashboard", DashboardView, "Sales, receivables and stock at a glance.", null, null),
        new("Sales", "Orders & quotations", OrdersView, "See and print orders and quotations.", OrdersEdit, "Create, change, quote, confirm and cancel them."),
        new("Sales", "Smart order entry", SmartEntryUse, "Read orders from email, WhatsApp, a PDF or a photo with AI. Needs Orders: Add & change.", null, null),
        new("Sales", "Invoices", InvoicesView, "See and print invoices.", InvoicesEdit, "Raise invoices from confirmed orders, and cancel them. Needs Orders: View."),
        new("Sales", "Payments", PaymentsView, "See payments received.", PaymentsEdit, "Record payments, from the invoice. Needs Invoices: View."),
        new("Sales", "Credit notes", CreditNotesView, "See and print credit notes, and the credit customers have with us.", CreditNotesEdit,
            "Issue credit notes from invoices for returns and price corrections, set credit against invoices, record refunds and cancel credit notes. Needs Invoices: View."),
        new("Purchases", "Purchase orders", PurchaseOrdersView, "See and print purchase orders and what has arrived against them.", PurchaseOrdersEdit, "Create and change orders, place them with suppliers, close and cancel them. Needs Suppliers and Products: View."),
        new("Purchases", "Supplier bills", SupplierBillsView, "See supplier bills, what we owe and the price and quantity checks.", SupplierBillsEdit, "Enter bills against purchase orders, and cancel them. Needs Purchase orders: View."),
        new("Purchases", "Supplier payments", SupplierPaymentsView, "See payments made to suppliers.", SupplierPaymentsEdit, "Record payments to suppliers, from the bill. Needs Supplier bills: View."),
        new("Purchases", "Supplier returns", SupplierReturnsView, "See goods returned to suppliers (debit notes) and the credit we have with them.", SupplierReturnsEdit,
            "Return goods against supplier bills, set credit against their bills, record refunds and cancel returns. Needs Supplier bills: View."),
        new("Masters", "Customers", CustomersView, "See customers and what they owe.", CustomersEdit, "Add and change customers. Credit terms need their own right."),
        new("Masters", "Suppliers", SuppliersView, "See suppliers and what we owe them.", SuppliersEdit, "Add and change suppliers."),
        new("Masters", "Products", ProductsView, "See products and selling prices.", ProductsEdit, "Add and change products."),
        new("Masters", "Rate updates", RatesView, "See each product's rate for each period.", RatesEdit, "Set rates for a period, including from Excel, and change or remove them."),
        new("Masters", "Categories & HSN", CatalogView, "See product categories and HSN codes.", CatalogEdit, "Add and change them."),
        new("Inventory", "Stock", StockView, "See stock levels, movements and goods receipts.", StockEdit, "Receive goods against purchase orders, and make stock adjustments. Needs Purchase orders: View."),
        new("Insights", "Reports", ReportsView, "Run and export the standard reports.", null, null),
        new("Insights", "Ask your data", AskDataUse, "Ask questions in plain English. Answers can draw on all sales, customer, product, stock and payment figures, and on purchases for those who can see cost prices.", null, null),
        new("Insights", "AI insights", InsightsView, "See the AI summary, forecast and alerts, and refresh them.", null, null),
        new("Insights", "Assistant", AssistantUse, "Chat with the assistant. It only looks up what this user may see.", null, null),
        new("Administration", "Users & roles", UsersView, "See users and roles.", UsersEdit, "Add and change users and roles, and reset passwords."),
        new("Administration", "Company settings", SettingsEdit, "Change the company's details, GSTIN and bank details.", null, null),
        new("Administration", "AI usage", AiUsageView, "See AI spend, every call to Claude and every query the AI wrote.", null, null),
        new("Administration", "Audit log", AuditView, "See who changed what and when, with old and new values, plus sign-ins and downloads; and each record's history.", null, null),
    ];

    public static readonly IReadOnlyList<PermissionRightDto> SpecialRights =
    [
        new(CostPricesView, "See cost prices and margins", "Cost prices, stock value and gross margin, in screens, reports, Ask your data and the assistant."),
        new(CreditTermsEdit, "Set credit terms", "Change a customer's credit limit, payment terms and sales rep."),
        new(CreditLimitOverride, "Override credit limits", "Confirm an order that takes a customer over their credit limit."),
        new(SalesRepAssignable, "Can be a sales rep", "Can be assigned customers as their sales rep."),
        new(PurchaseOrdersApprove, "Approve purchase orders", "Approve orders above the approval limit in Company settings, and place them without waiting for approval."),
        new(SupplierBillsApprove, "Approve bill differences", "Accept a supplier bill whose prices, quantities or totals don't match the order, so it can be paid."),
        new(BillReadingUse, "Read supplier bills with AI", "Fill in a supplier bill from a PDF or photo of their invoice. Uses AI credit."),
        new(AllCustomersActivity, "Hear about every customer", "Notifications about every customer's orders, payments and overdue invoices, not just the customers they look after."),
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
