import { afterEach, describe, expect, it, vi } from 'vitest'
import { authStore } from './auth-store'

const inHours = (hours: number) => new Date(Date.now() + hours * 3_600_000).toISOString()

describe('authStore', () => {
  afterEach(() => {
    authStore.clear()
    authStore.takeSignOutReason()
    vi.useRealTimers()
  })

  it('stores the session and exposes the token', () => {
    authStore.set({ token: 'abc', expiresAtUtc: inHours(8), userId: 'u-1' })

    expect(authStore.getToken()).toBe('abc')
    expect(JSON.parse(sessionStorage.getItem('salesapp.session')!)).toMatchObject({ token: 'abc' })
  })

  it('returns the same snapshot reference until the session changes', () => {
    authStore.set({ token: 'abc', expiresAtUtc: inHours(8), userId: 'u-1' })
    expect(authStore.getSession()).toBe(authStore.getSession())
  })

  it('does not hand out a token that is past its expiry', () => {
    authStore.set({ token: 'old', expiresAtUtc: inHours(-1), userId: 'u-1' })
    expect(authStore.getToken()).toBeNull()
  })

  it('signs out automatically when the token expires and remembers why', () => {
    vi.useFakeTimers()
    const listener = vi.fn()
    const unsubscribe = authStore.subscribe(listener)
    authStore.set({ token: 'abc', expiresAtUtc: inHours(1), userId: 'u-1' })
    listener.mockClear()

    vi.advanceTimersByTime(3_600_000 + 1)

    expect(authStore.getSession()).toBeNull()
    expect(listener).toHaveBeenCalledTimes(1)
    expect(authStore.takeSignOutReason()).toBe('expired')
    expect(authStore.takeSignOutReason()).toBeNull()
    unsubscribe()
  })

  it('clears storage on sign-out', () => {
    authStore.set({ token: 'abc', expiresAtUtc: inHours(8), userId: 'u-1' })
    authStore.clear('signed-out')

    expect(authStore.getSession()).toBeNull()
    expect(sessionStorage.getItem('salesapp.session')).toBeNull()
    expect(authStore.takeSignOutReason()).toBe('signed-out')
  })

  it('remembers whose session ended until the login page reads it', () => {
    authStore.set({ token: 'abc', expiresAtUtc: inHours(8), userId: 'u-7' })
    authStore.clear('expired')

    expect(authStore.lastSessionEnd()).toEqual({ reason: 'expired', userId: 'u-7' })
    expect(authStore.takeSignOutReason()).toBe('expired')
    expect(authStore.lastSessionEnd()).toBeNull()
  })

  it('treats a tab reloaded after its token ran out as an expired session', async () => {
    sessionStorage.setItem('salesapp.session', JSON.stringify({ token: 'old', expiresAtUtc: inHours(-1), userId: 'u-7' }))
    vi.resetModules()
    const { authStore: reloaded } = await import('./auth-store')

    expect(reloaded.getSession()).toBeNull()
    expect(reloaded.lastSessionEnd()).toEqual({ reason: 'expired', userId: 'u-7' })
    expect(sessionStorage.getItem('salesapp.session')).toBeNull()
  })
})
