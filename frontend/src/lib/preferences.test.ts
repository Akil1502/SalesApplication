import { afterEach, describe, expect, it } from 'vitest'
import { DEVICE_KEY, loadPreferences, parsePreferences, resolveMotion, resolveTheme, savePreferences } from './preferences'

afterEach(() => localStorage.clear())

describe('preferences', () => {
  it('falls back to the defaults field by field', () => {
    expect(parsePreferences(null)).toEqual({ theme: 'system' })
    expect(parsePreferences('not json')).toEqual({ theme: 'system' })
    expect(parsePreferences('{"theme":"purple","motion":"subtle"}')).toEqual({ theme: 'system', motion: 'subtle' })
    expect(parsePreferences('{"theme":"dark","motion":"wild"}')).toEqual({ theme: 'dark' })
    expect(parsePreferences('{"theme":"dark","layout":"dock"}')).toEqual({ theme: 'dark', layout: 'dock' })
    expect(parsePreferences('{"layout":"sideways"}')).toEqual({ theme: 'system' })
    expect(parsePreferences('{"language":"ta"}')).toEqual({ theme: 'system', language: 'ta' })
    expect(parsePreferences('{"language":"fr"}')).toEqual({ theme: 'system' })
  })

  it("keeps each user's choice, and the device's last choice for the sign-in page", () => {
    savePreferences('asha', { theme: 'dark', motion: 'off' })
    savePreferences('ravi', { theme: 'light' })

    expect(loadPreferences('asha')).toEqual({ theme: 'dark', motion: 'off' })
    expect(loadPreferences('ravi')).toEqual({ theme: 'light' })
    expect(loadPreferences(undefined)).toEqual({ theme: 'light' })
    expect(JSON.parse(localStorage.getItem(DEVICE_KEY)!)).toEqual({ theme: 'light' })
    // Someone new on this device starts from the device's last choice.
    expect(loadPreferences('meena')).toEqual({ theme: 'light' })
  })

  it('follows Windows until the user picks', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveMotion(undefined, true)).toBe('off')
    expect(resolveMotion(undefined, false)).toBe('full')
    expect(resolveMotion('full', true)).toBe('full')
  })
})
