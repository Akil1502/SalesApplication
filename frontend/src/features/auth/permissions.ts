/**
 * Permission keys; must match SalesApp.Core.Security.Permissions on the server. The server checks them on every
 * request; the web app only uses them to decide which menus, pages and buttons to show.
 */
export const Permissions = {
  DashboardView: 'dashboard.view',
  OrdersView: 'orders.view',
  OrdersEdit: 'orders.edit',
  SmartEntryUse: 'smart-entry.use',
  InvoicesView: 'invoices.view',
  InvoicesEdit: 'invoices.edit',
  PaymentsView: 'payments.view',
  PaymentsEdit: 'payments.edit',
  CustomersView: 'customers.view',
  CustomersEdit: 'customers.edit',
  ProductsView: 'products.view',
  ProductsEdit: 'products.edit',
  RatesView: 'rates.view',
  RatesEdit: 'rates.edit',
  CatalogView: 'catalog.view',
  CatalogEdit: 'catalog.edit',
  StockView: 'stock.view',
  StockEdit: 'stock.edit',
  ReportsView: 'reports.view',
  AskDataUse: 'ask-data.use',
  InsightsView: 'insights.view',
  AssistantUse: 'assistant.use',
  UsersView: 'users.view',
  UsersEdit: 'users.edit',
  SettingsEdit: 'settings.edit',
  AiUsageView: 'ai-usage.view',
  AuditView: 'audit.view',
  CostPricesView: 'cost-prices.view',
  CreditTermsEdit: 'credit-terms.edit',
  CreditLimitOverride: 'credit-limit.override',
  SalesRepAssignable: 'sales-rep.assignable',
} as const

export type Permission = (typeof Permissions)[keyof typeof Permissions]

/** The built-in Administrator role's name: it always has every permission. */
export const AdministratorRole = 'Administrator'
