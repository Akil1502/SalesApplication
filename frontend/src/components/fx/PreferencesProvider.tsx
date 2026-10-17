import { MotionConfig } from 'motion/react'
import { useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '@/features/auth/auth-context'
import {
  DEFAULT_LAYOUT,
  loadPreferences,
  parsePreferences,
  resolveMotion,
  resolveTheme,
  savePreferences,
  type LayoutChoice,
  type MotionLevel,
  type Preferences,
  type ThemeChoice,
} from '@/lib/preferences'
import { useUserSetting } from '@/lib/user-settings'
import { PreferencesContext, useMediaQuery, type PreferencesContextValue } from './preferences-context'

const NO_SERVER_PREFERENCES = null

/**
 * Applies light/dark, the motion level and the layout, and remembers each user's choice. The choice is kept on the
 * server (so it follows the person to any computer) with a copy in this browser for the very first paint and for
 * the sign-in page. Inside the auth provider: signing in switches to that user's own preferences.
 */
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const userId = user?.id
  const [stored, setStored] = useState(() => ({ userId, preferences: loadPreferences(userId) }))
  // Another user signed in (or out): switch to what this browser remembers for them until the server answers.
  if (stored.userId !== userId) {
    setStored({ userId, preferences: loadPreferences(userId) })
  }

  const [serverPreferences, saveServerPreferences, loaded] = useUserSetting<unknown>('prefs', NO_SERVER_PREFERENCES)
  // Once loaded, the server's copy is the one that counts.
  const preferences = useMemo<Preferences>(
    () => (loaded && serverPreferences ? parsePreferences(JSON.stringify(serverPreferences)) : stored.preferences),
    [loaded, serverPreferences, stored.preferences],
  )

  // Keep this browser's copy in step for the next first paint; the first time, move this browser's choice up.
  useEffect(() => {
    if (!loaded || !userId) return
    if (serverPreferences) savePreferences(userId, preferences)
    else saveServerPreferences(stored.preferences)
  }, [loaded, userId, serverPreferences, preferences, stored.preferences, saveServerPreferences])

  const systemDark = useMediaQuery('(prefers-color-scheme: dark)')
  const systemReducesMotion = useMediaQuery('(prefers-reduced-motion: reduce)')
  const resolvedTheme = resolveTheme(preferences.theme, systemDark)
  const motion = resolveMotion(preferences.motion, systemReducesMotion)

  useLayoutEffect(() => {
    const root = document.documentElement
    root.classList.toggle('dark', resolvedTheme === 'dark')
    root.style.colorScheme = resolvedTheme
    root.dataset.motion = motion
  }, [resolvedTheme, motion])

  const value = useMemo<PreferencesContextValue>(() => {
    const update = (patch: Partial<Preferences>) => {
      const next = { ...preferences, ...patch }
      setStored({ userId, preferences: next })
      savePreferences(userId, next)
      if (userId) saveServerPreferences(next)
    }
    return {
      theme: preferences.theme,
      resolvedTheme,
      setTheme: (theme: ThemeChoice) => update({ theme }),
      motion,
      motionChosen: preferences.motion !== undefined,
      setMotion: (level: MotionLevel) => update({ motion: level }),
      layout: preferences.layout ?? DEFAULT_LAYOUT,
      setLayout: (layout: LayoutChoice) => update({ layout }),
    }
  }, [preferences, resolvedTheme, motion, userId, saveServerPreferences])

  return (
    <PreferencesContext value={value}>
      <MotionConfig reducedMotion={motion === 'off' ? 'always' : 'never'}>{children}</MotionConfig>
    </PreferencesContext>
  )
}
