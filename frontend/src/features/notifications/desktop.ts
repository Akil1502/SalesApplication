import { useCallback, useState } from 'react'
import { useAuth } from '@/features/auth/auth-context'

const supported = () => typeof window !== 'undefined' && 'Notification' in window

const storageKey = (userId: string | undefined) => `salesapp.notify.desktop.${userId ?? 'anonymous'}`

function readOn(userId: string | undefined): boolean {
  try {
    return supported() && localStorage.getItem(storageKey(userId)) === '1' && Notification.permission === 'granted'
  } catch {
    return false
  }
}

/**
 * Windows (desktop) notifications for the important ones, off until the person turns them on in the bell. Kept per
 * browser rather than on the server, because the browser itself asks for (and remembers) the permission.
 */
export function useDesktopNotifications() {
  const userId = useAuth().user?.id
  const [on, setOnState] = useState(() => readOn(userId))
  const [permission, setPermission] = useState<NotificationPermission | 'unsupported'>(() => (supported() ? Notification.permission : 'unsupported'))

  const setOn = useCallback(
    async (next: boolean) => {
      if (!supported()) return
      let granted = Notification.permission
      if (next && granted === 'default') granted = await Notification.requestPermission()
      setPermission(granted)
      const enabled = next && granted === 'granted'
      try {
        localStorage.setItem(storageKey(userId), enabled ? '1' : '0')
      } catch {
        // Private browsing: it lasts until the page is closed.
      }
      setOnState(enabled)
    },
    [userId],
  )

  return { on: on && permission === 'granted', setOn, permission }
}
