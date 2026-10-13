import { Navigate, Outlet, useLocation } from 'react-router'
import { Spinner } from '@/components/ui/spinner'
import { useAuth } from './auth-context'

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

/** Route guard for role-restricted sections. The API enforces the same rule; this only hides the UI. */
export function RequireRole({ roles }: { roles: string[] }) {
  const { hasRole } = useAuth()
  return hasRole(...roles) ? <Outlet /> : <Navigate to="/" replace />
}
