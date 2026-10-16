import { createContext, useContext, useSyncExternalStore } from 'react'
import type { LayoutChoice, MotionLevel, ThemeChoice } from '@/lib/preferences'

export interface PreferencesContextValue {
  theme: ThemeChoice
  resolvedTheme: 'light' | 'dark'
  setTheme: (theme: ThemeChoice) => void
  /** The motion level in effect: the user's choice, or what Windows asks for. */
  motion: MotionLevel
  /** Whether the user picked the level (false: following Windows). */
  motionChosen: boolean
  setMotion: (motion: MotionLevel) => void
  layout: LayoutChoice
  setLayout: (layout: LayoutChoice) => void
}

export const PreferencesContext = createContext<PreferencesContextValue | null>(null)

export function usePreferences(): PreferencesContextValue {
  const value = useContext(PreferencesContext)
  if (!value) throw new Error('usePreferences must be used inside <PreferencesProvider>.')
  return value
}

/** The motion level in effect. Outside the provider (tests, print pages) motion is off. */
export function useMotionLevel(): MotionLevel {
  return useContext(PreferencesContext)?.motion ?? 'off'
}

export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      if (typeof window.matchMedia !== 'function') return () => {}
      const list = window.matchMedia(query)
      list.addEventListener('change', onChange)
      return () => list.removeEventListener('change', onChange)
    },
    () => typeof window.matchMedia === 'function' && window.matchMedia(query).matches,
    () => false,
  )
}

/** Light or dark as shown. Outside the provider (tests) it's light. */
export function useResolvedTheme(): 'light' | 'dark' {
  return useContext(PreferencesContext)?.resolvedTheme ?? 'light'
}
