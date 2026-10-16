import { MotionConfig } from 'motion/react'
import { useCallback, useLayoutEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '@/features/auth/auth-context'
import { loadPreferences, resolveMotion, resolveTheme, savePreferences, type MotionLevel, type Preferences, type ThemeChoice } from '@/lib/preferences'
import { PreferencesContext, useMediaQuery, type PreferencesContextValue } from './preferences-context'

/**
 * Applies light/dark and the motion level to the page and remembers each user's choice. Inside the auth provider:
 * signing in switches to that user's own preferences.
 */
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const userId = user?.id
  const [stored, setStored] = useState(() => ({ userId, preferences: loadPreferences(userId) }))
  // Another user signed in (or out): switch to their preferences.
  if (stored.userId !== userId) {
    setStored({ userId, preferences: loadPreferences(userId) })
  }

  const systemDark = useMediaQuery('(prefers-color-scheme: dark)')
  const systemReducesMotion = useMediaQuery('(prefers-reduced-motion: reduce)')
  const { preferences } = stored
  const resolvedTheme = resolveTheme(preferences.theme, systemDark)
  const motion = resolveMotion(preferences.motion, systemReducesMotion)

  useLayoutEffect(() => {
    const root = document.documentElement
    root.classList.toggle('dark', resolvedTheme === 'dark')
    root.style.colorScheme = resolvedTheme
    root.dataset.motion = motion
  }, [resolvedTheme, motion])

  const update = useCallback(
    (patch: Partial<Preferences>) =>
      setStored((current) => {
        const next = { ...current.preferences, ...patch }
        savePreferences(current.userId, next)
        return { ...current, preferences: next }
      }),
    [],
  )

  const value = useMemo<PreferencesContextValue>(
    () => ({
      theme: preferences.theme,
      resolvedTheme,
      setTheme: (theme: ThemeChoice) => update({ theme }),
      motion,
      motionChosen: preferences.motion !== undefined,
      setMotion: (level: MotionLevel) => update({ motion: level }),
    }),
    [preferences.theme, preferences.motion, resolvedTheme, motion, update],
  )

  return (
    <PreferencesContext value={value}>
      <MotionConfig reducedMotion={motion === 'off' ? 'always' : 'never'}>{children}</MotionConfig>
    </PreferencesContext>
  )
}
