import { useContext, type ReactNode } from 'react'
import { fill, msg, translator, type Values } from '@/lib/i18n'
import { DocumentLanguageContext, PrintLanguageOverride, useDocumentLanguage, type DocumentLanguage } from './document-language'

/** Gives one document its language: the one picked on the print screen, else its own. */
export function DocumentLanguageScope({ language, children }: { language: DocumentLanguage; children: ReactNode }) {
  const picked = useContext(PrintLanguageOverride)
  return <DocumentLanguageContext value={picked ?? language}>{children}</DocumentLanguageContext>
}

/**
 * A heading or column label in the document's language; on a both-language document, the Tamil sits under the
 * English. `inline` keeps both on one line ("No / இல்லை") for a word inside a line of text.
 */
export function DocText({ text, values, inline }: { text: string; values?: Values; inline?: boolean }) {
  const language = useDocumentLanguage()
  const english = fill(text, values)
  if (language === 'English') return <>{english}</>
  const tamil = translator('ta').t(text, values)
  if (language === 'Tamil') return <span lang="ta">{tamil}</span>
  if (tamil === english) return <>{english}</>
  if (inline) {
    return (
      <>
        {english} / <span lang="ta">{tamil}</span>
      </>
    )
  }
  return (
    <>
      {english}
      <span lang="ta" className="block text-[0.85em] leading-tight font-normal">
        {tamil}
      </span>
    </>
  )
}

const IN_WORDS = msg('Amount in words:')

/** The total in words: English, Tamil, or a line of each on a both-language document. */
export function DocAmountInWords({ english, tamil }: { english: string; tamil: string }) {
  const language = useDocumentLanguage()
  return (
    <>
      {language !== 'Tamil' && (
        <p>
          <b>{IN_WORDS}</b> {english}
        </p>
      )}
      {language !== 'English' && (
        <p lang="ta">
          <b>{translator('ta').t(IN_WORDS)}</b> {tamil}
        </p>
      )}
    </>
  )
}

/**
 * A label and its value on one line ("Invoice no. INV/26-27/0042"). On a both-language document the Tamil label goes
 * under the line, so the value stays beside the English.
 */
export function DocField({ label, children, className }: { label: string; children?: ReactNode; className?: string }) {
  const language = useDocumentLanguage()
  const tamil = translator('ta').t(label)
  return (
    <span className={className}>
      <b lang={language === 'Tamil' ? 'ta' : undefined}>{language === 'Tamil' ? tamil : label}</b> {children}
      {language === 'Both' && tamil !== label && (
        <span lang="ta" className="block text-[0.85em] leading-tight text-neutral-600">
          {tamil}
        </span>
      )}
    </span>
  )
}
