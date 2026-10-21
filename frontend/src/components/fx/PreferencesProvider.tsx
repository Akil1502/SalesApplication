import { MotionConfig } from 'motion/react'
import { useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '@/features/auth/auth-context'
import { setLanguage, type Language } from '@/lib/i18n'
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

/** A language picked on the sign-in page, to carry into the account once signed in. */
const PICKED_KEY = 'salesapp.language.picked'

function takePickedLanguage(): Language | undefined {
  try {
    const picked = sessionStorage.getItem(PICKED_KEY)
    sessionStorage.removeItem(PICKED_KEY)
    return picked === 'en' || picked === 'ta' ? picked : undefined
  } catch {
    return undefined
  }
}

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

  // Keep this browser's copy in step for the next first paint; the first time, move this browser's choice up. A
  // language picked on the sign-in page just now wins over the one saved last time.
  useEffect(() => {
    if (!loaded || !userId) return
    const picked = takePickedLanguage()
    if (serverPreferences && !picked) savePreferences(userId, preferences)
    else {
      const next = picked ? { ...(serverPreferences ? preferences : stored.preferences), language: picked } : stored.preferences
      savePreferences(userId, next)
      saveServerPreferences(next)
    }
  }, [loaded, userId, serverPreferences, preferences, stored.preferences, saveServerPreferences])

  const language = preferences.language ?? 'en'
  useEffect(() => {
    void setLanguage(language)
  }, [language])

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
      language,
      setLanguage: (next: Language) => {
        if (!userId) {
          try {
            sessionStorage.setItem(PICKED_KEY, next)
          } catch {
            // Without storage the choice still applies until the page closes.
          }
        }
        update({ language: next })
      },
    }
  }, [preferences, resolvedTheme, motion, language, userId, saveServerPreferences])

  return (
    <PreferencesContext value={value}>
      <MotionConfig reducedMotion={motion === 'off' ? 'always' : 'never'}>{children}</MotionConfig>
    </PreferencesContext>
  )
}
