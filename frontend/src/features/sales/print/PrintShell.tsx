import { Printer } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { useBranding } from '@/features/branding/api'

/**
 * A4 page for printed documents: no app chrome, black on white regardless of theme, and a toolbar that
 * disappears when printing. "Save as PDF" in the browser's print dialog produces the PDF.
 */
export function PrintShell({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="print-root min-h-svh bg-neutral-100 py-6 text-neutral-900 print:bg-white print:py-0">
      <div className="no-print mx-auto mb-4 flex max-w-[210mm] items-center justify-between px-2">
        <p className="text-sm text-neutral-600">{title}: use your browser's print dialog to print or save as PDF.</p>
        <Button onClick={() => window.print()}>
          <Printer />
          Print / Save PDF
        </Button>
      </div>
      <article className="mx-auto w-full max-w-[210mm] bg-white p-[12mm] text-[11px] leading-snug shadow print:max-w-none print:p-0 print:shadow-none">
        {children}
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
