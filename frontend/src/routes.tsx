import { createBrowserRouter, type RouteObject } from 'react-router'
import { AppLayout } from '@/components/layout/AppLayout'
import { navigation } from '@/components/layout/navigation'
import { LoginPage } from '@/features/auth/LoginPage'
import { RequireAuth, RequireRole } from '@/features/auth/RequireAuth'
import { ComingSoonPage } from '@/pages/ComingSoonPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { NotFoundPage } from '@/pages/NotFoundPage'

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
        children: [{ index: true, element: <DashboardPage /> }, ...placeholderRoutes],
      },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]

export const router = createBrowserRouter(routes)
