import { useSyncExternalStore } from 'react'
import { DEVICE_KEY } from '@/lib/preferences'

/**
 * English or Tamil, chosen by each person (see PreferencesProvider). English text in the code is the key: `t('Save')`
 * shows "Save" in English and looks up "சேமி" in src/locales/ta.json in Tamil. Anything missing falls back to the
 * English, and src/test/i18n.test.ts fails the build when a text has no Tamil yet.
 *
 * - `t('Saved {name}', { name })` fills in {placeholders}.
 * - `tn(count, '{count} invoice', '{count} invoices')` picks singular or plural; {count} is filled in.
 * - `tc('status', 'Open')` gives the same English a second Tamil meaning when it differs by context.
 * - `msg('Draft')` marks text defined outside a component (labels, schema messages) so it's collected for
 *   translation; translate it with `t(label)` where it's shown.
 *
 * Never call t() at module level: it would be fixed in whatever language was on when the file loaded. Changing
 * language re-renders the whole app (see LanguageBoundary), so t() during rendering is always current.
 */

export type Language = 'en' | 'ta'
export type Values = Record<string, string | number>

type Entry = string | { one: string; other: string }
type Catalog = Record<string, Entry>

export const LANGUAGES: readonly { value: Language; label: string }[] = [
  { value: 'en', label: 'English' }, // i18n-ignore: each language is named in its own script
  { value: 'ta', label: 'தமிழ்' },
]

export const isLanguage = (value: unknown): value is Language => value === 'en' || value === 'ta'

/** The language this browser used last (the device copy of the preferences), so the first paint is right. */
function deviceLanguage(): Language {
  try {
    const prefs = JSON.parse(localStorage.getItem(DEVICE_KEY) ?? '{}') as { language?: unknown } | null
    return isLanguage(prefs?.language) ? prefs.language : 'en'
  } catch {
    return 'en'
  }
}

let current: Language = deviceLanguage()
let tamil: Catalog | null = null
const listeners = new Set<() => void>()

/** The Tamil texts load only when someone uses Tamil, so English users never download them. */
async function loadCatalog(language: Language): Promise<void> {
  if (language === 'ta' && !tamil) tamil = (await import('@/locales/ta.json')).default as Catalog
}

export function getLanguage(): Language {
  return current
}

/** Switches language once its texts have loaded; everything subscribed (the whole app) re-renders. */
export async function setLanguage(language: Language): Promise<void> {
  await loadCatalog(language)
  document.documentElement.lang = language
  if (language === current) return
  current = language
  for (const listener of listeners) listener()
}

/** Loads the Tamil texts without changing the screen's language (a Tamil printed document in an English app). */
export function loadTamil(): Promise<void> {
  return loadCatalog('ta')
}

export function hasTamil(): boolean {
  return tamil !== null
}

/** Loads the starting language's texts before the first render (see main.tsx). */
export function prepareLanguage(): Promise<void> {
  return setLanguage(current)
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useLanguage(): Language {
  return useSyncExternalStore(subscribe, getLanguage, getLanguage)
}

/** Use in tests to render in Tamil with a given catalog. */
export function setCatalogForTests(catalog: Catalog | null, language: Language) {
  tamil = catalog
  current = language
}

const PLACEHOLDER = /\{(\w+)\}/g

export function fill(text: string, values?: Values): string {
  if (!values) return text
  return text.replace(PLACEHOLDER, (whole, name: string) => (name in values ? String(values[name]) : whole))
}

const countFormatter = new Intl.NumberFormat('en-IN')
const pluralRules = { en: new Intl.PluralRules('en-IN'), ta: new Intl.PluralRules('ta-IN') }

/** Text for one language, independent of the one on screen (printed documents follow the customer's language). */
export function translator(language: Language) {
  const lookup = (key: string): Entry | undefined => (language === 'ta' ? tamil?.[key] : undefined)
  const pick = (entry: Entry | undefined, fallback: string, count: number): string => {
    if (entry === undefined) return fallback
    if (typeof entry === 'string') return entry
    return pluralRules[language].select(count) === 'one' ? entry.one : entry.other
  }

  const translate = (text: string, values?: Values): string => fill(pick(lookup(text), text, 0), values)
  const translateCount = (count: number, one: string, other: string, values?: Values): string => {
    const english = count === 1 ? one : other
    return fill(pick(lookup(other), english, count), { count: countFormatter.format(count), ...values })
  }
  const translateIn = (context: string, text: string, values?: Values): string => fill(pick(lookup(`${context}::${text}`), text, 0), values)

  return { t: translate, tn: translateCount, tc: translateIn }
}

/** Translates into the language on screen. */
export function t(text: string, values?: Values): string {
  return translator(current).t(text, values)
}

/** Singular or plural in the language on screen; the plural English is the key. */
export function tn(count: number, one: string, other: string, values?: Values): string {
  return translator(current).tn(count, one, other, values)
}

/** Same English, different meaning: the context keeps the Tamil apart ("status::Open" vs "Open"). */
export function tc(context: string, text: string, values?: Values): string {
  return translator(current).tc(context, text, values)
}

/** Marks text for translation where it's defined; it returns the English unchanged. */
export function msg<T extends string>(text: T): T {
  return text
}

/** The header value for the API, which answers messages in the same language. */
export function acceptLanguage(): string {
  return current === 'ta' ? 'ta-IN,en-IN;q=0.5' : 'en-IN'
}
