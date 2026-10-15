import { ShieldOff } from 'lucide-react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { allowedNavItems } from '@/components/layout/navigation'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Spinner } from '@/components/ui/spinner'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from './auth-context'
import { Permissions } from './permissions'

/** Route guard: renders child routes only for a signed-in user, otherwise sends them to /login. */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div className="flex min-h-svh items-center justify-center">
        <Spinner className="size-6 text-muted-foreground" />
      </div>
    )
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  return <Outlet />
}

/** Route guard for permission-restricted pages. The API enforces the same rule; this only hides the UI. */
export function RequirePermission({ anyOf }: { anyOf: string[] }) {
  const { can } = useAuth()
  return can(...anyOf) ? <Outlet /> : <Navigate to="/" replace />
}

/**
 * The home page is the dashboard for those who may see it. Anyone else goes to the first page they may open, so a
 * user with only AI usage lands on AI usage; a user with no pages at all is told so.
 */
export function HomeGate() {
  const { can } = useAuth()
  const ai = useAiStatus()
  if (can(Permissions.DashboardView)) return <Outlet />
  if (ai.isPending) return null

  const first = allowedNavItems(can, ai.data?.enabled ?? false)[0]
  if (first) return <Navigate to={first.to} replace />
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <ShieldOff />
        </EmptyMedia>
        <EmptyTitle>No pages yet</EmptyTitle>
        <EmptyDescription>Your account doesn't have access to any part of SalesApp yet. Ask an administrator to give you access.</EmptyDescription>
      </EmptyHeader>
    </Empty>
  )
}
