import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useCallback } from 'react'
import { toast } from 'sonner'
import { useAuth } from '@/features/auth/auth-context'
import { api } from '@/lib/api/client'
import { errorMessage, unwrap } from '@/lib/api/errors'

/**
 * Each person's own settings, kept on the server so they follow them to any computer (see the UserSettings API).
 * Reading is one request when the app starts; a change shows at once and is saved in the background, so typing or
 * toggling never waits for the network.
 */

type Settings = Record<string, unknown>

const settingsKey = (userId: string | undefined) => ['me', 'settings', userId] as const

export function useUserSettings() {
  const { status, user } = useAuth()
  return useQuery({
    queryKey: settingsKey(user?.id),
    queryFn: async () => (await unwrap(api.GET('/api/me/settings'))) as Settings,
    enabled: status === 'authenticated',
    staleTime: Infinity,
  })
}

// Saves are held briefly per setting, so a burst of changes (dragging, typing a name) is one request.
const pending = new Map<string, ReturnType<typeof setTimeout>>()

function scheduleSave(queryClient: QueryClient, userId: string, key: string, value: unknown) {
  clearTimeout(pending.get(key))
  pending.set(
    key,
    setTimeout(() => {
      pending.delete(key)
      unwrap(api.PUT('/api/me/settings/{key}', { params: { path: { key } }, body: value as never })).catch((error: unknown) => {
        toast.error(`Couldn't save your setting: ${errorMessage(error)}`)
        void queryClient.invalidateQueries({ queryKey: settingsKey(userId) })
      })
    }, 500),
  )
}

/**
 * One setting, with what to use until it's been set (pass a constant, not a new array or object each render).
 * Returns the value, a setter (a value, or a function of the current value) and whether the saved settings have
 * loaded yet.
 */
export function useUserSetting<T>(key: string, fallback: T): [T, (next: T | ((current: T) => T)) => void, boolean] {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const settings = useUserSettings()
  const value = (settings.data?.[key] as T | undefined) ?? fallback

  const set = useCallback(
    (next: T | ((current: T) => T)) => {
      if (!user) return
      queryClient.setQueryData<Settings>(settingsKey(user.id), (current) => {
        const previous = (current?.[key] as T | undefined) ?? fallback
        const resolved = typeof next === 'function' ? (next as (current: T) => T)(previous) : next
        scheduleSave(queryClient, user.id, key, resolved)
        return { ...current, [key]: resolved }
      })
    },
    [key, queryClient, user, fallback],
  )

  return [value, set, settings.isSuccess]
}
