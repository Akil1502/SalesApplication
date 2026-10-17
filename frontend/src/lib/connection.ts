import { useSyncExternalStore } from 'react'
import { ApiRequestError } from './api/errors'

/**
 * Whether the SalesApp server answered lately. Requests report in (see main.tsx): a request that couldn't reach the
 * server marks it unreachable, and any successful one marks it reachable again.
 */
let unreachable = false
const listeners = new Set<() => void>()

function set(value: boolean) {
  if (unreachable === value) return
  unreachable = value
  listeners.forEach((listener) => listener())
}

/** True for failures that mean the server itself couldn't be reached (rather than it refusing the request). */
export function isUnreachable(error: unknown): boolean {
  return error instanceof ApiRequestError && (error.status === 0 || error.status === 502 || error.status === 503 || error.status === 504)
}

export const connection = {
  reportError(error: unknown) {
    if (isUnreachable(error)) set(true)
  },
  reportSuccess() {
    set(false)
  },
}

export function useServerUnreachable(): boolean {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => unreachable,
    () => false,
  )
}

export function useOnline(): boolean {
  return useSyncExternalStore(
    (listener) => {
      window.addEventListener('online', listener)
      window.addEventListener('offline', listener)
      return () => {
        window.removeEventListener('online', listener)
        window.removeEventListener('offline', listener)
      }
    },
    () => navigator.onLine,
    () => true,
  )
}
