/**
 * Display preferences: light or dark, how much motion, and how the menus are laid out. They're remembered per user
 * in this browser, plus a copy for the device so the sign-in page (and the first paint, see public/theme-init.js)
 * use the last choice.
 */

export type ThemeChoice = 'light' | 'dark' | 'system'
export type MotionLevel = 'full' | 'subtle' | 'off'
/** Where the menus live: a rail on the left, mega menus across the top, or a dock at the bottom. */
export type LayoutChoice = 'rail' | 'top' | 'dock'

export interface Preferences {
  theme: ThemeChoice
  /** Unset until the user picks one: until then the Windows "reduce animations" setting decides. */
  motion?: MotionLevel
  layout?: LayoutChoice
}

export const DEFAULT_PREFERENCES: Preferences = { theme: 'system' }
export const DEFAULT_LAYOUT: LayoutChoice = 'rail'

/** Read by public/theme-init.js before the app loads; keep the two in step. */
export const DEVICE_KEY = 'salesapp.prefs.device'

const userKey = (userId: string) => `salesapp.prefs.${userId}`

const THEMES: readonly ThemeChoice[] = ['light', 'dark', 'system']
const MOTION_LEVELS: readonly MotionLevel[] = ['full', 'subtle', 'off']
const LAYOUTS: readonly LayoutChoice[] = ['rail', 'top', 'dock']

/** Anything unreadable falls back to the defaults, field by field. */
export function parsePreferences(raw: string | null | undefined): Preferences {
  if (!raw) return { ...DEFAULT_PREFERENCES }
  try {
    const value = JSON.parse(raw) as Partial<Record<keyof Preferences, unknown>>
    const theme = THEMES.find((t) => t === value.theme) ?? DEFAULT_PREFERENCES.theme
    const motion = MOTION_LEVELS.find((m) => m === value.motion)
    const layout = LAYOUTS.find((l) => l === value.layout)
    return { theme, ...(motion && { motion }), ...(layout && { layout }) }
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
