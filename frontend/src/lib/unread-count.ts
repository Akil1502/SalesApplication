import { useSyncExternalStore } from 'react'

// How many notifications the signed-in person hasn't read: the bell sets it, the browser tab's title shows it.
let count = 0
const listeners = new Set<() => void>()

export function setUnreadCount(next: number) {
  if (next === count) return
  count = next
  for (const listener of listeners) listener()
}

export function useUnreadCount(): number {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => count,
    () => 0,
  )
}
