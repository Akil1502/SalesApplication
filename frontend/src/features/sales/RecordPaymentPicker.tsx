import { Search } from 'lucide-react'
import { useState } from 'react'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { t, tn } from '@/lib/i18n'
import { useDebounced } from '@/lib/use-debounced'
import { cn } from '@/lib/utils'
import { useInvoices, type InvoiceListItem } from './api'
import { RecordPaymentDialog } from './RecordPaymentDialog'

/**
 * Record a payment without opening the invoice first: pick the invoice (the most overdue are listed until you
 * search), then the usual payment form. Used by the dashboard's quick actions and the phone's ＋ menu.
 */
export function RecordPaymentPicker({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [search, setSearch] = useState('')
  const [chosen, setChosen] = useState<InvoiceListItem | null>(null)
  const q = useDebounced(search.trim(), 250)
  const invoices = useInvoices(q ? { q, page: 1, pageSize: 10 } : { overdueOnly: true, sort: 'dueDate', page: 1, pageSize: 8 })
  const due = (invoices.data?.items ?? []).filter((invoice) => invoice.balance > 0 && invoice.status !== 'Cancelled')

  const close = (next: boolean) => {
    if (!next) setSearch('')
    onOpenChange(next)
  }

  return (
    <>
      <Dialog open={open && !chosen} onOpenChange={close}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{t('Record a payment')}</DialogTitle>
            <DialogDescription>{t('Which invoice is it for? The most overdue are listed; search by number or customer for others.')}</DialogDescription>
          </DialogHeader>
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
            <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder={t('Invoice number or customer')} className="pl-9" aria-label={t('Find an invoice')} autoFocus />
          </div>
          <ul className="-mx-2 flex max-h-80 flex-col overflow-y-auto" aria-label={t('Invoices with money due')}>
            {invoices.isPending
              ? Array.from({ length: 4 }, (_, i) => (
                  <li key={i} className="p-2">
                    <Skeleton className="h-10 w-full" />
                  </li>
                ))
              : due.map((invoice) => (
                  <li key={invoice.id}>
                    <button
                      type="button"
                      className="flex w-full items-center gap-3 rounded-lg px-2 py-2 text-left text-sm hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
                      onClick={() => setChosen(invoice)}
                    >
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{invoice.customerName}</span>
                        <span className="block text-xs text-muted-foreground">
                          {t('{invoice} · due {date}', { invoice: invoice.number, date: formatBusinessDate(invoice.dueDate) })}
                        </span>
                      </span>
                      <span className="text-right">
                        <span className="block font-medium tabular-nums">{formatINR(invoice.balance)}</span>
                        {!!invoice.daysOverdue && (
                          <span className={cn('block text-xs', invoice.daysOverdue > 30 ? 'text-destructive' : 'text-muted-foreground')}>
                            {tn(invoice.daysOverdue, '{count} day late', '{count} days late')}
                          </span>
                        )}
                      </span>
                    </button>
                  </li>
                ))}
            {!invoices.isPending && due.length === 0 && (
              <li className="px-2 py-6 text-center text-sm text-muted-foreground">{q ? t('No invoice with money due matches.') : t('Nothing is overdue.')}</li>
            )}
          </ul>
        </DialogContent>
      </Dialog>
      {chosen && (
        <RecordPaymentDialog
          invoice={chosen}
          open
          onOpenChange={(next) => {
            if (next) return
            setChosen(null)
            close(false)
          }}
        />
      )}
    </>
  )
}
