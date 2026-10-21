import { useQueries } from '@tanstack/react-query'
import { Fragment, useEffect } from 'react'
import { useSearchParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api/client'
import { errorMessage, unwrap } from '@/lib/api/errors'
import { t, tn } from '@/lib/i18n'
import { PrintShell } from './PrintShell'
import { InvoiceDocument } from './InvoicePrintPage'

/** At most this many invoices in one print run, so the page stays quick to prepare. */
const MAX_INVOICES = 50

/** Several invoices in one print run (ticked on the invoice list), each starting on a new page in its customer's language. */
export function InvoicesPrintPage() {
  const [params] = useSearchParams()
  const ids = (params.get('ids') ?? '')
    .split(',')
    .map(Number)
    .filter((id) => Number.isInteger(id) && id > 0)
    .slice(0, MAX_INVOICES)

  const invoices = useQueries({
    queries: ids.map((id) => ({
      queryKey: ['invoices', 'detail', id],
      queryFn: () => unwrap(api.GET('/api/invoices/{id}', { params: { path: { id } } })),
    })),
  })

  useEffect(() => {
    const previous = document.title
    document.title = tn(ids.length, '{count} invoice', '{count} invoices')
    return () => {
      document.title = previous
    }
  }, [ids.length])

  if (ids.length === 0) return <p className="m-8">{t('No invoices were chosen to print.')}</p>
  if (invoices.some((q) => q.isPending)) return <Skeleton className="m-8 h-[60vh]" />
  const failed = invoices.find((q) => q.error)
  if (failed) return <p className="m-8 text-red-700">{errorMessage(failed.error)}</p>

  return (
    <PrintShell title={tn(ids.length, '{count} invoice', '{count} invoices')} languages={invoices.flatMap((q) => (q.data ? [q.data.documentLanguage] : []))}>
      {invoices.map((q, index) => (
        <Fragment key={ids[index]}>
          {index > 0 && <div aria-hidden className="my-[12mm] border-t-2 border-dashed border-neutral-300 [break-before:page] print:my-0 print:border-0" />}
          {q.data && <InvoiceDocument inv={q.data} />}
        </Fragment>
      ))}
    </PrintShell>
  )
}
