import { Printer } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { SegmentedControl } from '@/components/SegmentedControl'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useBranding } from '@/features/branding/api'
import { msg, t } from '@/lib/i18n'
import { PrintLanguageOverride, useTamilTexts, type DocumentLanguage } from './document-language'

type Choice = DocumentLanguage | 'own'

const CHOICES: { value: DocumentLanguage; label: ReactNode }[] = [
  { value: 'English', label: 'English' }, // i18n-ignore: each language is named in its own script
  { value: 'Tamil', label: <span lang="ta">தமிழ்</span> },
  { value: 'Both', label: msg('Both') },
]

/**
 * A4 page for printed documents: no app chrome, black on white regardless of theme, and a toolbar that
 * disappears when printing. "Save as PDF" in the browser's print dialog produces the PDF. Each document prints in
 * its customer's (or supplier's) language, given in `languages`; the toolbar can switch them all for this copy.
 */
export function PrintShell({ title, languages, children }: { title: string; languages: DocumentLanguage[]; children: ReactNode }) {
  const mixed = new Set(languages).size > 1
  const [choice, setChoice] = useState<Choice>('own')
  const picked = choice === 'own' ? null : choice
  const ready = useTamilTexts(picked ? picked !== 'English' : languages.some((l) => l !== 'English'))
  const shown: Choice = picked ?? (mixed ? 'own' : (languages[0] ?? 'English'))

  return (
    <div className="print-root min-h-svh bg-neutral-100 py-6 text-neutral-900 print:bg-white print:py-0">
      <div className="no-print mx-auto mb-4 flex max-w-[210mm] flex-wrap items-center justify-between gap-3 px-2">
        <p className="text-sm text-neutral-600">{t("{title}: use your browser's print dialog to print or save as PDF.", { title })}</p>
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-neutral-600">{t('Print in')}</span>
          <SegmentedControl<Choice>
            label={t('Print in')}
            value={shown}
            onValueChange={setChoice}
            options={mixed ? [{ value: 'own', label: msg("Each customer's language") }, ...CHOICES] : CHOICES}
          />
          <Button onClick={() => window.print()}>
            <Printer />
            {t('Print / Save PDF')}
          </Button>
        </div>
      </div>
      <article className="mx-auto w-full max-w-[210mm] bg-white p-[12mm] text-[11px] leading-snug shadow print:max-w-none print:p-0 print:shadow-none">
        <PrintLanguageOverride value={picked}>{ready ? children : <Skeleton className="h-[60vh]" />}</PrintLanguageOverride>
      </article>
    </div>
  )
}

/** The company's logo above its name on printed documents, when it has one. */
export function PrintLogo() {
  const logoUrl = useBranding().data?.logoUrl
  return logoUrl ? <img src={logoUrl} alt="" className="mb-1.5 block h-[14mm] w-auto max-w-[50mm] object-contain object-left" /> : null
}

/** Bordered table cell styles shared by printed documents. */
export const cell = 'border border-neutral-400 px-1.5 py-1 align-top'
export const headCell = `${cell} bg-neutral-100 text-left font-semibold`
export const numCell = `${cell} text-right tabular-nums`
