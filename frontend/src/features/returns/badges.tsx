import { Badge } from '@/components/ui/badge'
import { formatINR } from '@/lib/format'
import { t } from '@/lib/i18n'
import type { ReturnDocumentStatus } from './api'
import { returnStatusLabels } from './labels'

export function ReturnStatusBadge({ status }: { status: ReturnDocumentStatus }) {
  return (
    <Badge
      variant="outline"
      className={
        status === 'Cancelled'
          ? 'border-red-600/30 bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300'
          : 'border-emerald-600/30 bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300'
      }
    >
      {t(returnStatusLabels[status])}
    </Badge>
  )
}

/** Credit not yet set against anything or refunded. */
export function CreditLeftBadge({ amount }: { amount: number }) {
  if (amount <= 0) return null
  return (
    <Badge variant="outline" className="border-violet-600/30 bg-violet-50 text-violet-800 dark:bg-violet-950 dark:text-violet-300">
      {t('{amount} credit left', { amount: formatINR(amount) })}
    </Badge>
  )
}

/** Marks a credit note or return issued after the GST time limit, which credits the value only. */
export function NoGstBadge() {
  return (
    <Badge variant="outline" className="border-amber-600/30 bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-300">
      {t('Value only, no GST')}
    </Badge>
  )
}
