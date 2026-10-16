/**
 * Display preferences: light or dark, and how much motion. They're remembered per user in this browser, plus a
 * copy for the device so the sign-in page (and the first paint, see public/theme-init.js) use the last choice.
 */

export type ThemeChoice = 'light' | 'dark' | 'system'
export type MotionLevel = 'full' | 'subtle' | 'off'

export interface Preferences {
  theme: ThemeChoice
  /** Unset until the user picks one: until then the Windows "reduce animations" setting decides. */
  motion?: MotionLevel
}

export const DEFAULT_PREFERENCES: Preferences = { theme: 'system' }

/** Read by public/theme-init.js before the app loads; keep the two in step. */
export const DEVICE_KEY = 'salesapp.prefs.device'

const userKey = (userId: string) => `salesapp.prefs.${userId}`

const THEMES: readonly ThemeChoice[] = ['light', 'dark', 'system']
const MOTION_LEVELS: readonly MotionLevel[] = ['full', 'subtle', 'off']

/** Anything unreadable falls back to the defaults, field by field. */
export function parsePreferences(raw: string | null | undefined): Preferences {
  if (!raw) return { ...DEFAULT_PREFERENCES }
  try {
    const value = JSON.parse(raw) as Partial<Record<keyof Preferences, unknown>>
    const theme = THEMES.find((t) => t === value.theme) ?? DEFAULT_PREFERENCES.theme
    const motion = MOTION_LEVELS.find((m) => m === value.motion)
    return motion ? { theme, motion } : { theme }
  } catch {
    return { ...DEFAULT_PREFERENCES }
  }
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Private windows and blocked storage: the choice lasts until the page closes.
  }
}

/** A user's own choice, else the device's last choice, else the defaults. */
export function loadPreferences(userId: string | undefined): Preferences {
  return parsePreferences((userId && read(userKey(userId))) || read(DEVICE_KEY))
}

export function savePreferences(userId: string | undefined, preferences: Preferences) {
  const json = JSON.stringify(preferences)
  if (userId) write(userKey(userId), json)
  write(DEVICE_KEY, json)
}

export function resolveTheme(choice: ThemeChoice, systemDark: boolean): 'light' | 'dark' {
  return choice === 'system' ? (systemDark ? 'dark' : 'light') : choice
}

export function resolveMotion(choice: MotionLevel | undefined, systemReducesMotion: boolean): MotionLevel {
  return choice ?? (systemReducesMotion ? 'off' : 'full')
}
