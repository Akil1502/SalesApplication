import { createContext, useContext, useEffect, useState } from 'react'
import type { Schemas } from '@/lib/api/client'
import { fill, hasTamil, loadTamil, t, translator, type Language, type Values } from '@/lib/i18n'

/**
 * Printed documents follow the customer's (or supplier's) language, not the screen's: English, Tamil, or both, with
 * each label in English and the Tamil under it. The print screen can switch every document on it for a one-off copy.
 */
export type DocumentLanguage = Schemas['DocumentLanguage']

/** The language picked on the print screen for every document on it; null keeps each document's own. */
export const PrintLanguageOverride = createContext<DocumentLanguage | null>(null)

/** The language of the document being drawn. */
export const DocumentLanguageContext = createContext<DocumentLanguage>('English')

export const useDocumentLanguage = () => useContext(DocumentLanguageContext)

/** Dates name their month in Tamil on a Tamil document; a both-language document keeps English dates. */
export const dateLanguage = (language: DocumentLanguage): Language => (language === 'Tamil' ? 'ta' : 'en')

/** Text that has to be one string (the PDF's file name): English, Tamil, or "English / Tamil". */
export function docText(language: DocumentLanguage, text: string, values?: Values): string {
  const english = fill(text, values)
  if (language === 'English') return english
  const tamil = translator('ta').t(text, values)
  return language === 'Tamil' || tamil === english ? tamil : `${english} / ${tamil}`
}

/** Whether the Tamil texts are ready; they're loaded on demand when a document needs them in an English app. */
export function useTamilTexts(needed: boolean): boolean {
  const [loaded, setLoaded] = useState(hasTamil)
  useEffect(() => {
    if (needed && !loaded) void loadTamil().then(() => setLoaded(true))
  }, [needed, loaded])
  return loaded || !needed
}

/** The choices for a customer's, supplier's or the company's document language. */
export function documentLanguageOptions(): { value: DocumentLanguage; label: string }[] {
  return [
    { value: 'English', label: 'English' }, // i18n-ignore: each language is named in its own script
    { value: 'Tamil', label: 'தமிழ்' },
    { value: 'Both', label: t('English and Tamil') },
  ]
}
