import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppLayout } from '@/components/layout/AppLayout'
import { navigation } from '@/components/layout/navigation'
import { Roles } from '@/features/auth/auth-context'
import { LoginPage } from '@/features/auth/LoginPage'
import { RequireAuth, RequireRole } from '@/features/auth/RequireAuth'
import { ComingSoonPage } from '@/pages/ComingSoonPage'
import { DashboardPage } from '@/pages/DashboardPage'
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
  { path: '/stock', lazy: () => import('@/features/inventory/StockPage').then((m) => ({ Component: m.StockPage })) },
  { path: '/stock/receipts/:id', lazy: () => import('@/features/inventory/ReceiptDetailPage').then((m) => ({ Component: m.ReceiptDetailPage })) },
  {
    element: <RequireRole roles={managers} />,
    children: [
      { path: '/products/new', lazy: () => import('@/features/products/ProductFormPage').then((m) => ({ Component: m.ProductFormPage })) },
      { path: '/catalog', lazy: () => import('@/features/catalog/CatalogPage').then((m) => ({ Component: m.CatalogPage })) },
      { path: '/stock/receipts/new', lazy: () => import('@/features/inventory/ReceiptFormPage').then((m) => ({ Component: m.ReceiptFormPage })) },
    ],
  },
  {
    element: <RequireRole roles={admins} />,
    children: [
      { path: '/admin/users', lazy: () => import('@/features/users/UsersPage').then((m) => ({ Component: m.UsersPage })) },
      { path: '/admin/settings', lazy: () => import('@/features/settings/CompanySettingsPage').then((m) => ({ Component: m.CompanySettingsPage })) },
    ],
  },
]

/**
 * Screens that aren't built yet get a placeholder route generated from the navigation config,
 * wrapped in a role guard when the item is role-restricted. Each phase replaces its placeholders.
 */
const placeholderRoutes: RouteObject[] = navigation
  .flatMap((group) => group.items)
  .filter((item) => item.phase)
  .map((item) => {
    const page: RouteObject = {
      path: item.to,
      element: <ComingSoonPage title={item.title} phase={item.phase} />,
    }
    return item.roles ? { element: <RequireRole roles={item.roles} />, children: [page] } : page
  })

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppLayout />,
        children: [{ index: true, element: <DashboardPage /> }, ...featureRoutes, ...placeholderRoutes],
      },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]

export const router = createBrowserRouter(routes)
