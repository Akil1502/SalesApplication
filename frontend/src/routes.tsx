import type { ComponentType } from 'react'
import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppLayout } from '@/components/layout/AppLayout'
import { RouteError } from '@/components/errors/RouteError'
import { RecordPanelLayout } from '@/components/layout/RecordPanelLayout'
import { Spinner } from '@/components/ui/spinner'
import { LoginPage } from '@/features/auth/LoginPage'
import { Permissions as P } from '@/features/auth/permissions'
import { HomeGate, RequireAuth, RequirePermission } from '@/features/auth/RequireAuth'
import { NotFoundPage } from '@/pages/NotFoundPage'

/**
 * Feature screens are code-split: each page's JavaScript downloads the first time it's opened,
 * so signing in doesn't pay for every screen up front. Records (an invoice, a customer) open in a panel beside their
 * list: their routes are children of the list's route (see withPanel).
 */
const featureRoutes: RouteObject[] = [
  guarded([P.CustomersView], [
    withPanel('/customers', () => import('@/features/customers/CustomersPage').then((m) => m.CustomersPage), [
      guarded([P.CustomersEdit], [{ path: 'new', lazy: () => import('@/features/customers/CustomerFormPage').then((m) => ({ Component: m.CustomerFormPage })) }]),
      { path: ':id', lazy: () => import('@/features/customers/CustomerFormPage').then((m) => ({ Component: m.CustomerFormPage })) },
    ]),
  ]),
  guarded([P.SuppliersView], [
    withPanel('/suppliers', () => import('@/features/purchasing/SuppliersPage').then((m) => m.SuppliersPage), [
      guarded([P.SuppliersEdit], [{ path: 'new', lazy: () => import('@/features/purchasing/SupplierFormPage').then((m) => ({ Component: m.SupplierFormPage })) }]),
      { path: ':id', lazy: () => import('@/features/purchasing/SupplierFormPage').then((m) => ({ Component: m.SupplierFormPage })) },
    ]),
  ]),
  guarded([P.PurchaseOrdersView], [
    withPanel('/purchase-orders', () => import('@/features/purchasing/PurchaseOrdersPage').then((m) => m.PurchaseOrdersPage), [
      guarded([P.PurchaseOrdersEdit], [
        { path: 'new', lazy: () => import('@/features/purchasing/PurchaseOrderFormPage').then((m) => ({ Component: m.PurchaseOrderFormPage })) },
      ]),
      { path: ':id', lazy: () => import('@/features/purchasing/PurchaseOrderFormPage').then((m) => ({ Component: m.PurchaseOrderFormPage })) },
    ]),
  ]),
  guarded([P.SupplierBillsView], [
    withPanel('/supplier-bills', () => import('@/features/purchasing/SupplierBillsPage').then((m) => m.SupplierBillsPage), [
      guarded([P.SupplierBillsEdit], [
        { path: 'new', lazy: () => import('@/features/purchasing/SupplierBillFormPage').then((m) => ({ Component: m.SupplierBillFormPage })) },
      ]),
      { path: ':id', lazy: () => import('@/features/purchasing/SupplierBillDetailPage').then((m) => ({ Component: m.SupplierBillDetailPage })) },
      guarded([P.SupplierReturnsEdit], [
        { path: ':id/return', lazy: () => import('@/features/returns/SupplierReturnFormPage').then((m) => ({ Component: m.SupplierReturnFormPage })) },
      ]),
    ]),
  ]),
  guarded([P.SupplierPaymentsView], [
    { path: '/supplier-payments', lazy: () => import('@/features/purchasing/SupplierPaymentsPage').then((m) => ({ Component: m.SupplierPaymentsPage })) },
  ]),
  guarded([P.SupplierReturnsView], [
    withPanel('/supplier-returns', () => import('@/features/returns/SupplierReturnsPage').then((m) => m.SupplierReturnsPage), [
      { path: ':id', lazy: () => import('@/features/returns/SupplierReturnDetailPage').then((m) => ({ Component: m.SupplierReturnDetailPage })) },
    ]),
  ]),
  guarded([P.ProductsView], [
    withPanel('/products', () => import('@/features/products/ProductsPage').then((m) => m.ProductsPage), [
      guarded([P.ProductsEdit], [{ path: 'new', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) }]),
      { path: ':id', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) },
    ]),
  ]),
  guarded([P.RatesView], [{ path: '/rates', lazy: () => import('@/features/rates/RatesPage').then((m) => ({ Component: m.RatesPage })) }]),
  guarded([P.CatalogView], [{ path: '/catalog', lazy: () => import('@/features/catalog/CatalogPage').then((m) => ({ Component: m.CatalogPage })) }]),
  guarded([P.OrdersView], [
    withPanel('/orders', () => import('@/features/sales/OrdersPage').then((m) => m.OrdersPage), [
      guarded([P.OrdersEdit], [{ path: 'new', lazy: () => import('@/features/sales/OrderFormPage').then((m) => ({ Component: m.OrderFormPage })) }]),
      { path: ':id', lazy: () => import('@/features/sales/OrderFormPage').then((m) => ({ Component: m.OrderFormPage })) },
    ]),
  ]),
  guarded([P.SmartEntryUse], [
    { path: '/orders/smart-entry', lazy: () => import('@/features/sales/smart-entry/SmartOrderPage').then((m) => ({ Component: m.SmartOrderPage })) },
  ]),
  guarded([P.InvoicesView], [
    withPanel('/invoices', () => import('@/features/sales/InvoicesPage').then((m) => m.InvoicesPage), [
      { path: ':id', lazy: () => import('@/features/sales/InvoiceDetailPage').then((m) => ({ Component: m.InvoiceDetailPage })) },
      guarded([P.CreditNotesEdit], [
        { path: ':id/credit-note', lazy: () => import('@/features/returns/CreditNoteFormPage').then((m) => ({ Component: m.CreditNoteFormPage })) },
      ]),
    ]),
  ]),
  guarded([P.PaymentsView], [{ path: '/payments', lazy: () => import('@/features/sales/PaymentsPage').then((m) => ({ Component: m.PaymentsPage })) }]),
  guarded([P.CreditNotesView], [
    withPanel('/credit-notes', () => import('@/features/returns/CreditNotesPage').then((m) => m.CreditNotesPage), [
      { path: ':id', lazy: () => import('@/features/returns/CreditNoteDetailPage').then((m) => ({ Component: m.CreditNoteDetailPage })) },
    ]),
  ]),
  guarded([P.StockView], [
    withPanel('/stock', () => import('@/features/inventory/StockPage').then((m) => m.StockPage), [
      { path: 'receipts/:id', lazy: () => import('@/features/inventory/ReceiptDetailPage').then((m) => ({ Component: m.ReceiptDetailPage })) },
    ]),
  ]),
  guarded([P.ReportsView], [
    { path: '/reports', lazy: () => import('@/features/reports/ReportsPage').then((m) => ({ Component: m.ReportsPage })) },
    { path: '/reports/:key', lazy: () => import('@/features/reports/ReportPage').then((m) => ({ Component: m.ReportPage })) },
  ]),
  guarded([P.AskDataUse], [{ path: '/ask', lazy: () => import('@/features/ask/AskPage').then((m) => ({ Component: m.AskPage })) }]),
  guarded([P.InsightsView], [{ path: '/insights', lazy: () => import('@/features/insights/InsightsPage').then((m) => ({ Component: m.InsightsPage })) }]),
  guarded([P.UsersView], [{ path: '/admin/users', lazy: () => import('@/features/users/UsersPage').then((m) => ({ Component: m.UsersPage })) }]),
  guarded([P.SettingsEdit], [
    { path: '/admin/settings', lazy: () => import('@/features/settings/CompanySettingsPage').then((m) => ({ Component: m.CompanySettingsPage })) },
  ]),
  guarded([P.AuditView], [{ path: '/admin/audit', lazy: () => import('@/features/audit/AuditLogPage').then((m) => ({ Component: m.AuditLogPage })) }]),
  guarded([P.AiUsageView], [{ path: '/admin/ai-usage', lazy: () => import('@/features/ai-usage/AiUsagePage').then((m) => ({ Component: m.AiUsagePage })) }]),
]

/** A list page whose records (the child routes) open in a slide-over panel beside it. */
function withPanel(list: string, load: () => Promise<ComponentType>, children: RouteObject[]): RouteObject {
  return {
    path: list,
    lazy: () =>
      load().then((List) => ({
        Component: () => (
          <RecordPanelLayout list={list}>
            <List />
          </RecordPanelLayout>
        ),
      })),
    // A record that fails shows its error in the panel, leaving the list usable.
    children: [{ errorElement: <RouteError variant="panel" />, children }],
  }
}

/** Routes that need any one of these permissions; anyone else is sent home. */
function guarded(anyOf: string[], children: RouteObject[]): RouteObject {
  return { element: <RequirePermission anyOf={anyOf} />, children }
}

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage />, errorElement: <RouteError variant="full" /> },
  {
    element: <RequireAuth />,
    errorElement: <RouteError variant="full" />,
    // Shown while the first screen's code-split chunk downloads on a fresh page load.
    hydrateFallbackElement: (
      <div className="flex h-svh items-center justify-center">
        <Spinner className="size-6" />
      </div>
    ),
    children: [
      {
        element: <AppLayout />,
        children: [
          {
            // A page that fails shows its error inside the app, with the menus still there.
            errorElement: <RouteError />,
            children: [
              // The dashboard is lazy too: it carries the charting library, which no other screen needs. Users without
              // it land on the first page they may open.
              {
                element: <HomeGate />,
                children: [{ index: true, lazy: () => import('@/features/dashboard/DashboardPage').then((m) => ({ Component: m.DashboardPage })) }],
              },
              ...featureRoutes,
            ],
          },
        ],
      },
      // Printable documents render without the sidebar.
      guarded([P.InvoicesView], [
        { path: '/invoices/:id/print', lazy: () => import('@/features/sales/print/InvoicePrintPage').then((m) => ({ Component: m.InvoicePrintPage })) },
        { path: '/invoices/print', lazy: () => import('@/features/sales/print/InvoicesPrintPage').then((m) => ({ Component: m.InvoicesPrintPage })) },
      ]),
      guarded([P.OrdersView], [
        { path: '/orders/:id/print', lazy: () => import('@/features/sales/print/QuotationPrintPage').then((m) => ({ Component: m.QuotationPrintPage })) },
      ]),
      guarded([P.PurchaseOrdersView], [
        { path: '/purchase-orders/:id/print', lazy: () => import('@/features/purchasing/PurchaseOrderPrintPage').then((m) => ({ Component: m.PurchaseOrderPrintPage })) },
      ]),
      guarded([P.CreditNotesView], [
        { path: '/credit-notes/:id/print', lazy: () => import('@/features/returns/CreditNotePrintPage').then((m) => ({ Component: m.CreditNotePrintPage })) },
      ]),
      guarded([P.SupplierReturnsView], [
        { path: '/supplier-returns/:id/print', lazy: () => import('@/features/returns/DebitNotePrintPage').then((m) => ({ Component: m.DebitNotePrintPage })) },
      ]),
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]

export const router = createBrowserRouter(routes)
