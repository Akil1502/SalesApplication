import { useMemo, useSyncExternalStore } from 'react'
import type { Schemas } from '@/lib/api/client'
import { brandCss, brandTheme } from './brand-theme'

type Branding = Schemas['BrandingDto']

/** The last branding seen, for the next visit's first paint; public/theme-init.js reads it too. */
const STORAGE_KEY = 'salesapp.brand'

interface StoredBrand {
  companyName: string
  brandColour: string | null
  logoUrl: string | null
  /** The style sheet and the title as theme-init.js applies them before the app loads. */
  css: string
  title: string
}

export function cachedBranding(): Branding | undefined {
  try {
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as StoredBrand | null
    return stored && typeof stored.companyName === 'string'
      ? { companyName: stored.companyName, brandColour: stored.brandColour, logoUrl: stored.logoUrl }
      : undefined
  } catch {
    return undefined
  }
}

const sheetFor = (colour: string | null | undefined) => {
  const theme = colour ? brandTheme(colour) : null
  return theme ? brandCss(theme) : ''
}

function styleElement(): HTMLStyleElement {
  let element = document.getElementById('brand-theme') as HTMLStyleElement | null
  if (!element) {
    element = document.createElement('style')
    element.id = 'brand-theme'
    document.head.appendChild(element)
  }
  return element
}

function setFavicon(logoUrl: string | null | undefined) {
  const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
  if (!link) return
  if (logoUrl) {
    link.removeAttribute('type')
    link.href = logoUrl
  } else {
    link.type = 'image/svg+xml'
    link.href = '/favicon.svg'
  }
}

// The colour on screen: the saved one, or one being tried on the settings page before saving.
let saved: string | null = null
let preview: string | null | undefined
const listeners = new Set<() => void>()
const current = () => (preview === undefined ? saved : preview)

function paint() {
  styleElement().textContent = sheetFor(current())
  for (const listener of listeners) listener()
}

/** Applies the company's colour and logo, and keeps a copy for the next visit's first paint. */
export function applyBranding(branding: Branding, name: string) {
  saved = branding.brandColour ?? null
  paint()
  setFavicon(branding.logoUrl)
  try {
    const stored: StoredBrand = {
      companyName: branding.companyName,
      brandColour: branding.brandColour ?? null,
      logoUrl: branding.logoUrl ?? null,
      css: sheetFor(saved),
      title: name,
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
  } catch {
    // Private browsing: the colours still apply, just a moment later next time.
  }
}

/** Shows a colour without saving it (null: the standard indigo); undefined goes back to the saved one. */
export function previewBrandColour(colour: string | null | undefined) {
  preview = colour
  paint()
}

const subscribe = (listener: () => void) => {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** The brand colour on screen right now (null for the standard indigo). */
export function useBrandColour(): string | null {
  return useSyncExternalStore(subscribe, current, () => null)
}

/** The 3D scenes' main and second colour (#rrggbb) for the brand colour on screen; null keeps each scene's own. */
export function useSceneColours(dark: boolean): [string, string] | null {
  const colour = useBrandColour()
  return useMemo(() => {
    const theme = colour ? brandTheme(colour) : null
    return theme ? theme.scene[dark ? 'dark' : 'light'] : null
  }, [colour, dark])
}
