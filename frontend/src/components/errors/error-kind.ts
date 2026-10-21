import { isRouteErrorResponse } from 'react-router'
import { ApiRequestError } from '@/lib/api/errors'

export type ErrorKind = 'not-found' | 'new-version' | 'unreachable' | 'forbidden' | 'broken'

/** What went wrong, in terms that decide what to tell the user and what to offer. */
export function errorKind(error: unknown): ErrorKind {
  if (isRouteErrorResponse(error)) return error.status === 404 ? 'not-found' : error.status === 403 ? 'forbidden' : 'broken'
  if (error instanceof ApiRequestError) {
    if (error.status === 0 || error.status >= 502) return 'unreachable'
    if (error.status === 404) return 'not-found'
    if (error.status === 403) return 'forbidden'
  }
  // A page's code that can't be downloaded any more: the app was updated since this tab opened.
  const message = error instanceof Error ? error.message : ''
  if (/dynamically imported module|importing a module script failed|error loading dynamically imported/i.test(message)) return 'new-version'
  return 'broken'
}

/** The technical details, for copying to whoever looks after the app. */
export function errorDetails(error: unknown): string {
  if (isRouteErrorResponse(error)) return `${error.status} ${error.statusText}`
  if (error instanceof ApiRequestError) return `${error.status} ${error.message}${error.body.traceId ? ` (trace ${error.body.traceId})` : ''}`
  if (error instanceof Error) return `${error.name}: ${error.message}`
  return String(error)
}
