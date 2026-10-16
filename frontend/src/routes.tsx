import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppLayout } from '@/components/layout/AppLayout'
import { Spinner } from '@/components/ui/spinner'
import { LoginPage } from '@/features/auth/LoginPage'
import { Permissions as P } from '@/features/auth/permissions'
import { HomeGate, RequireAuth, RequirePermission } from '@/features/auth/RequireAuth'
import { NotFoundPage } from '@/pages/NotFoundPage'

/**
 * Feature screens are code-split: each page's JavaScript downloads the first time it's opened,
 * so signing in doesn't pay for every screen up front.
 */
const featureRoutes: RouteObject[] = [
  guarded([P.CustomersView], [
    { path: '/customers', lazy: () => import('@/features/customers/CustomersPage').then((m) => ({ Component: m.CustomersPage })) },
    { path: '/customers/:id', lazy: () => import('@/features/customers/CustomerFormPage').then((m) => ({ Component: m.CustomerFormPage })) },
  ]),
  guarded([P.CustomersEdit], [
    { path: '/customers/new', lazy: () => import('@/features/customers/CustomerFormPage').then((m) => ({ Component: m.CustomerFormPage })) },
  ]),
  guarded([P.ProductsView], [
    { path: '/products', lazy: () => import('@/features/products/ProductsPage').then((m) => ({ Component: m.ProductsPage })) },
    { path: '/products/:id', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) },
  ]),
  guarded([P.ProductsEdit], [
    { path: '/products/new', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) },
  ]),
  guarded([P.RatesView], [{ path: '/rates', lazy: () => import('@/features/rates/RatesPage').then((m) => ({ Component: m.RatesPage })) }]),
  guarded([P.CatalogView], [{ path: '/catalog', lazy: () => import('@/features/catalog/CatalogPage').then((m) => ({ Component: m.CatalogPage })) }]),
  guarded([P.OrdersView], [
    { path: '/orders', lazy: () => import('@/features/sales/OrdersPage').then((m) => ({ Component: m.OrdersPage })) },
    { path: '/orders/:id', lazy: () => import('@/features/sales/OrderFormPage').then((m) => ({ Component: m.OrderFormPage })) },
  ]),
  guarded([P.OrdersEdit], [{ path: '/orders/new', lazy: () => import('@/features/sales/OrderFormPage').then((m) => ({ Component: m.OrderFormPage })) }]),
  guarded([P.SmartEntryUse], [
    { path: '/orders/smart-entry', lazy: () => import('@/features/sales/smart-entry/SmartOrderPage').then((m) => ({ Component: m.SmartOrderPage })) },
  ]),
  guarded([P.InvoicesView], [
    { path: '/invoices', lazy: () => import('@/features/sales/InvoicesPage').then((m) => ({ Component: m.InvoicesPage })) },
    { path: '/invoices/:id', lazy: () => import('@/features/sales/InvoiceDetailPage').then((m) => ({ Component: m.InvoiceDetailPage })) },
  ]),
  guarded([P.PaymentsView], [{ path: '/payments', lazy: () => import('@/features/sales/PaymentsPage').then((m) => ({ Component: m.PaymentsPage })) }]),
  guarded([P.StockView], [
    { path: '/stock', lazy: () => import('@/features/inventory/StockPage').then((m) => ({ Component: m.StockPage })) },
    { path: '/stock/receipts/:id', lazy: () => import('@/features/inventory/ReceiptDetailPage').then((m) => ({ Component: m.ReceiptDetailPage })) },
  ]),
  guarded([P.StockEdit], [
    { path: '/stock/receipts/new', lazy: () => import('@/features/inventory/ReceiptFormPage').then((m) => ({ Component: m.ReceiptFormPage })) },
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

/** Routes that need any one of these permissions; anyone else is sent home. */
function guarded(anyOf: string[], children: RouteObject[]): RouteObject {
  return { element: <RequirePermission anyOf={anyOf} />, children }
}

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  {
    element: <RequireAuth />,
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
          // The dashboard is lazy too: it carries the charting library, which no other screen needs. Users without it
          // land on the first page they may open.
          {
            element: <HomeGate />,
            children: [{ index: true, lazy: () => import('@/features/dashboard/DashboardPage').then((m) => ({ Component: m.DashboardPage })) }],
          },
          ...featureRoutes,
        ],
      },
      // Printable documents render without the sidebar.
      guarded([P.InvoicesView], [
        { path: '/invoices/:id/print', lazy: () => import('@/features/sales/print/InvoicePrintPage').then((m) => ({ Component: m.InvoicePrintPage })) },
      ]),
      guarded([P.OrdersView], [
        { path: '/orders/:id/print', lazy: () => import('@/features/sales/print/QuotationPrintPage').then((m) => ({ Component: m.QuotationPrintPage })) },
      ]),
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]

export const router = createBrowserRouter(routes)
