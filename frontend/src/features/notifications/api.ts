import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '@/features/auth/auth-context'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type Notification = Schemas['NotificationDto']
export type NotificationList = Schemas['NotificationListDto']
export type NotificationKind = Schemas['NotificationKind']
export type NotificationKindSetting = Schemas['NotificationKindDto']

const listKey = ['notifications', 'list'] as const
const preferencesKey = ['notifications', 'preferences'] as const

/** How often the bell checks for new notifications (and straight away when the tab comes back into view). */
export const POLL_EVERY = 45_000

/**
 * The signed-in person's latest notifications. Checked every 45 seconds while the tab is open, and in the
 * background too when Windows notifications are on (so they can pop up while the browser is minimised).
 */
export function useNotifications({ background = false }: { background?: boolean } = {}) {
  const { status } = useAuth()
  return useQuery({
    queryKey: listKey,
    queryFn: () => unwrap(api.GET('/api/notifications', { params: { query: { take: 40 } } })),
    enabled: status === 'authenticated',
    refetchInterval: POLL_EVERY,
    refetchIntervalInBackground: background,
    refetchOnWindowFocus: true,
    staleTime: 15_000,
  })
}

function useUpdateList() {
  const queryClient = useQueryClient()
  return {
    set: (update: (list: NotificationList) => NotificationList) => queryClient.setQueryData<NotificationList>(listKey, (list) => (list ? update(list) : list)),
    refresh: () => queryClient.invalidateQueries({ queryKey: listKey }),
  }
}

export function useMarkRead() {
  const list = useUpdateList()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.POST('/api/notifications/{id}/read', { params: { path: { id } } })),
    onMutate: (id) =>
      list.set((current) => {
        const wasUnread = current.items.some((n) => n.id === id && !n.read)
        return { ...current, items: current.items.map((n) => (n.id === id ? { ...n, read: true } : n)), unreadCount: Math.max(0, current.unreadCount - (wasUnread ? 1 : 0)) }
      }),
    onSettled: list.refresh,
  })
}

export function useMarkAllRead() {
  const list = useUpdateList()
  return useMutation({
    mutationFn: (upToId: number) => unwrap(api.POST('/api/notifications/read-all', { body: { upToId } })),
    onMutate: (upToId) =>
      list.set((current) => {
        const items = current.items.map((n) => (n.id <= upToId ? { ...n, read: true } : n))
        return { ...current, items, unreadCount: items.filter((n) => !n.read).length }
      }),
    onSettled: list.refresh,
  })
}

/** The bell was opened: what's in it now is no longer "since your last visit" next time. */
export function useOpened() {
  const list = useUpdateList()
  return useMutation({
    mutationFn: (upToId: number) => unwrap(api.POST('/api/notifications/opened', { body: { upToId } })),
    onSuccess: (_, upToId) => list.set((current) => ({ ...current, lastOpenedUpToId: Math.max(current.lastOpenedUpToId, upToId) })),
  })
}

export function useNotificationPreferences(enabled: boolean) {
  return useQuery({ queryKey: preferencesKey, queryFn: () => unwrap(api.GET('/api/notifications/preferences')), enabled })
}

export function useSaveNotificationPreferences() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (muted: NotificationKind[]) => unwrap(api.PUT('/api/notifications/preferences', { body: { muted } })),
    onSuccess: (kinds) => {
      queryClient.setQueryData(preferencesKey, kinds)
      void queryClient.invalidateQueries({ queryKey: listKey })
    },
  })
}
