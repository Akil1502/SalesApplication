import { createContext, useContext } from 'react'
import type { Schemas } from '@/lib/api/client'

export type CurrentUser = Schemas['CurrentUserDto']

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

export interface AuthContextValue {
  status: AuthStatus
  user: CurrentUser | null
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  /** True when the user holds at least one of these permissions (see ./permissions). */
  can: (...anyOf: string[]) => boolean
}

export const AuthContext = createContext<AuthContextValue | null>(null)

/** TanStack Query key for GET /api/auth/me. */
export const currentUserQueryKey = ['auth', 'me'] as const

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) {
    throw new Error('useAuth must be used inside <AuthProvider>.')
  }
  return value
}
