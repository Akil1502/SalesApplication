import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppLayout } from '@/components/layout/AppLayout'
import { Spinner } from '@/components/ui/spinner'
import { Roles } from '@/features/auth/auth-context'
import { LoginPage } from '@/features/auth/LoginPage'
import { RequireAuth, RequireRole } from '@/features/auth/RequireAuth'
import { NotFoundPage } from '@/pages/NotFoundPage'

const managers = [Roles.Admin, Roles.SalesManager]
const admins = [Roles.Admin]

/**
 * Feature screens are code-split: each page's JavaScript downloads the first time it's opened,
 * so signing in doesn't pay for every screen up front.
 */
const featureRoutes: RouteObject[] = [
  { path: '/customers', lazy: () => import('@/features/customers/CustomersPage').then((m) => ({ Component: m.CustomersPage })) },
  { path: '/customers/new', lazy: () => import('@/features/customers/CustomerFormPage').then((m) => ({ Component: m.CustomerFormPage })) },
  { path: '/customers/:id', lazy: () => import('@/features/customers/CustomerFormPage').then((m) => ({ Component: m.CustomerFormPage })) },
  { path: '/products', lazy: () => import('@/features/products/ProductsPage').then((m) => ({ Component: m.ProductsPage })) },
  { path: '/products/:id', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) },
  { path: '/orders', lazy: () => import('@/features/sales/OrdersPage').then((m) => ({ Component: m.OrdersPage })) },
  { path: '/orders/new', lazy: () => import('@/features/sales/OrderFormPage').then((m) => ({ Component: m.OrderFormPage })) },
  { path: '/orders/smart-entry', lazy: () => import('@/features/sales/smart-entry/SmartOrderPage').then((m) => ({ Component: m.SmartOrderPage })) },
  { path: '/orders/:id', lazy: () => import('@/features/sales/OrderFormPage').then((m) => ({ Component: m.OrderFormPage })) },
  { path: '/invoices', lazy: () => import('@/features/sales/InvoicesPage').then((m) => ({ Component: m.InvoicesPage })) },
  { path: '/invoices/:id', lazy: () => import('@/features/sales/InvoiceDetailPage').then((m) => ({ Component: m.InvoiceDetailPage })) },
  { path: '/payments', lazy: () => import('@/features/sales/PaymentsPage').then((m) => ({ Component: m.PaymentsPage })) },
  { path: '/stock', lazy: () => import('@/features/inventory/StockPage').then((m) => ({ Component: m.StockPage })) },
  { path: '/stock/receipts/:id', lazy: () => import('@/features/inventory/ReceiptDetailPage').then((m) => ({ Component: m.ReceiptDetailPage })) },
  { path: '/reports', lazy: () => import('@/features/reports/ReportsPage').then((m) => ({ Component: m.ReportsPage })) },
  { path: '/reports/:key', lazy: () => import('@/features/reports/ReportPage').then((m) => ({ Component: m.ReportPage })) },
  { path: '/ask', lazy: () => import('@/features/ask/AskPage').then((m) => ({ Component: m.AskPage })) },
  {
    element: <RequireRole roles={managers} />,
    children: [
      { path: '/products/new', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) },
      { path: '/catalog', lazy: () => import('@/features/catalog/CatalogPage').then((m) => ({ Component: m.CatalogPage })) },
      { path: '/stock/receipts/new', lazy: () => import('@/features/inventory/ReceiptFormPage').then((m) => ({ Component: m.ReceiptFormPage })) },
      { path: '/insights', lazy: () => import('@/features/insights/InsightsPage').then((m) => ({ Component: m.InsightsPage })) },
    ],
  },
  {
    element: <RequireRole roles={admins} />,
    children: [
      { path: '/admin/users', lazy: () => import('@/features/users/UsersPage').then((m) => ({ Component: m.UsersPage })) },
      { path: '/admin/settings', lazy: () => import('@/features/settings/CompanySettingsPage').then((m) => ({ Component: m.CompanySettingsPage })) },
      { path: '/admin/ai-usage', lazy: () => import('@/features/ai-usage/AiUsagePage').then((m) => ({ Component: m.AiUsagePage })) },
    ],
  },
]

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
          // The dashboard is lazy too: it carries the charting library, which no other screen needs.
          { index: true, lazy: () => import('@/features/dashboard/DashboardPage').then((m) => ({ Component: m.DashboardPage })) },
          ...featureRoutes,
        ],
      },
      // Printable documents render without the sidebar.
      { path: '/invoices/:id/print', lazy: () => import('@/features/sales/print/InvoicePrintPage').then((m) => ({ Component: m.InvoicePrintPage })) },
      { path: '/orders/:id/print', lazy: () => import('@/features/sales/print/QuotationPrintPage').then((m) => ({ Component: m.QuotationPrintPage })) },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]

export const router = createBrowserRouter(routes)
