/**
 * Holds the signed-in session (JWT + expiry) outside React, so the API client can read the
 * token without hooks and any component can subscribe via useSyncExternalStore.
 *
 * Stored in sessionStorage: it survives a page reload but not closing the tab, and it is never
 * shared across tabs. The token is short-lived (8h) and carries no refresh capability.
 */

export interface Session {
  token: string
  expiresAtUtc: string
  /** Who signed in, so after an expired session only that person is taken back to the page they were on. */
  userId: string
}

export type SignOutReason = 'expired' | 'signed-out'

/** How the last session in this tab ended, and whose it was. */
export interface SessionEnd {
  reason: SignOutReason
  userId: string
}

type Listener = () => void

const STORAGE_KEY = 'salesapp.session'

const listeners = new Set<Listener>()
let lastEnd: SessionEnd | null = null
let current: Session | null = load()
let expiryTimer: ReturnType<typeof setTimeout> | undefined
scheduleExpiry()

function load(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<Session>
    if (typeof parsed.token !== 'string' || typeof parsed.expiresAtUtc !== 'string' || typeof parsed.userId !== 'string') return null
    if (isExpired(parsed.expiresAtUtc)) {
      // The tab was reloaded after the token ran out: the same as it expiring while open.
      lastEnd = { reason: 'expired', userId: parsed.userId }
      sessionStorage.removeItem(STORAGE_KEY)
      return null
    }
    return { token: parsed.token, expiresAtUtc: parsed.expiresAtUtc, userId: parsed.userId }
  } catch {
    // Storage blocked (privacy mode) or corrupt JSON: behave as signed out.
    return null
  }
}

function isExpired(expiresAtUtc: string, now = Date.now()): boolean {
  const expiry = Date.parse(expiresAtUtc)
  return Number.isNaN(expiry) || expiry <= now
}

function emit() {
  for (const listener of listeners) listener()
}

/** Sign out automatically the moment the token expires, so the UI never sits on a dead session. */
function scheduleExpiry() {
  clearTimeout(expiryTimer)
  if (!current) return
  // setTimeout's maximum delay is ~24.8 days; tokens live hours, but clamp anyway.
  const delay = Math.min(Math.max(Date.parse(current.expiresAtUtc) - Date.now(), 0), 2_147_483_647)
  expiryTimer = setTimeout(() => authStore.clear('expired'), delay)
}

export const authStore = {
  subscribe(listener: Listener): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },

  /**
   * Current session, or null when signed out. Used as a useSyncExternalStore snapshot, so it must be
   * pure and return the same reference until the session changes; expiry is handled by a timer.
   */
  getSession(): Session | null {
    return current
  },

  /** The bearer token, or null if signed out or already past expiry (belt and braces for the timer). */
  getToken(): string | null {
    return current && !isExpired(current.expiresAtUtc) ? current.token : null
  },

  set(session: Session) {
    current = session
    lastEnd = null
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session))
    } catch {
      // Keep the in-memory session even if storage is unavailable.
    }
    scheduleExpiry()
    emit()
  },

  clear(reason: SignOutReason = 'signed-out') {
    if (current === null) return
    lastEnd = { reason, userId: current.userId }
    current = null
    clearTimeout(expiryTimer)
    try {
      sessionStorage.removeItem(STORAGE_KEY)
    } catch {
      // Ignore: nothing persisted.
    }
    emit()
  },

  /** How the last session ended, so the sign-in redirect knows whether to come back to the same page. */
  lastSessionEnd(): SessionEnd | null {
    return lastEnd
  },

  /** Why the last session ended, so the login page can say "your session expired". Read once. */
  takeSignOutReason(): SignOutReason | null {
    const reason = lastEnd?.reason ?? null
    lastEnd = null
    return reason
  },
}
