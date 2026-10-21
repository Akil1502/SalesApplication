import { Badge } from '@/components/ui/badge'
import { t } from '@/lib/i18n'
import type { InvoiceStatus, OrderStatus } from './api'
import { invoiceStatusLabels, orderStatusLabels } from './labels'

const tone = {
  neutral: 'text-muted-foreground',
  blue: 'border-sky-600/30 bg-sky-50 text-sky-800 dark:bg-sky-950 dark:text-sky-300',
  green: 'border-emerald-600/30 bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300',
  amber: 'border-amber-600/30 bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
  violet: 'border-violet-600/30 bg-violet-50 text-violet-800 dark:bg-violet-950 dark:text-violet-300',
  red: 'border-red-600/30 bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300',
}

const orderTones: Record<OrderStatus, string> = {
  Draft: tone.neutral,
  Quoted: tone.blue,
  Confirmed: tone.violet,
  Invoiced: tone.green,
  Cancelled: tone.red,
}

export function OrderStatusBadge({ status }: { status: OrderStatus }) {
  return (
    <Badge variant="outline" className={orderTones[status]}>
      {t(orderStatusLabels[status])}
    </Badge>
  )
}

const invoiceTones: Record<InvoiceStatus, string> = {
  Unpaid: tone.amber,
  PartiallyPaid: tone.blue,
  Paid: tone.green,
  Cancelled: tone.red,
}

/** settledByCredit: nothing was paid and credit notes cleared it, so "Paid" would mislead. */
export function InvoiceStatusBadge({ status, daysOverdue, settledByCredit }: { status: InvoiceStatus; daysOverdue?: number | null; settledByCredit?: boolean }) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <Badge variant="outline" className={invoiceTones[status]}>
        {status === 'Paid' && settledByCredit ? t('Credited') : t(invoiceStatusLabels[status])}
      </Badge>
      {daysOverdue != null && (
        <Badge variant="outline" className={tone.red}>
          {t('{days}d overdue', { days: daysOverdue })}
        </Badge>
      )}
    </span>
  )
}
