import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { api } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { AuthContext, currentUserQueryKey, type AuthContextValue, type AuthStatus } from './auth-context'
import { authStore } from './auth-store'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const session = useSyncExternalStore(authStore.subscribe, authStore.getSession)

  // The token proves who we are; /me tells us the current role, permissions and whether the account is still active.
  const meQuery = useQuery({
    queryKey: currentUserQueryKey,
    queryFn: () => unwrap(api.GET('/api/auth/me')),
    enabled: session !== null,
    staleTime: 5 * 60_000,
    retry: false,
  })

  // When the session ends (sign-out, expiry, 401), drop every cached response from the old user.
  useEffect(() => {
    if (session === null) {
      queryClient.clear()
    }
  }, [session, queryClient])

  const login = useCallback(
    async (email: string, password: string) => {
      const result = await unwrap(api.POST('/api/auth/login', { body: { email, password } }))
      queryClient.setQueryData(currentUserQueryKey, result.user)
      authStore.set({ token: result.accessToken, expiresAtUtc: result.expiresAtUtc, userId: result.user.id })
    },
    [queryClient],
  )

  const logout = useCallback(() => authStore.clear('signed-out'), [])

  const value = useMemo<AuthContextValue>(() => {
    const user = session ? (meQuery.data ?? null) : null
    let status: AuthStatus = 'anonymous'
    if (session && user) status = 'authenticated'
    else if (session && meQuery.isPending) status = 'loading'

    return {
      status,
      user,
      login,
      logout,
      can: (...anyOf) => user !== null && anyOf.some((permission) => user.permissions.includes(permission)),
    }
  }, [session, meQuery.data, meQuery.isPending, login, logout])

  return <AuthContext value={value}>{children}</AuthContext>
}
