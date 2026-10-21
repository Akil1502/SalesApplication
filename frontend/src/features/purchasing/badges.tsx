import { Badge } from '@/components/ui/badge'
import { t } from '@/lib/i18n'
import type { BillMatchStatus, PurchaseOrderStatus, SupplierBillStatus } from './api'
import { billStatusLabels, matchStatusLabels, purchaseOrderStatusLabels } from './labels'

const tone = {
  neutral: 'text-muted-foreground',
  blue: 'border-sky-600/30 bg-sky-50 text-sky-800 dark:bg-sky-950 dark:text-sky-300',
  green: 'border-emerald-600/30 bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300',
  amber: 'border-amber-600/30 bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
  violet: 'border-violet-600/30 bg-violet-50 text-violet-800 dark:bg-violet-950 dark:text-violet-300',
  red: 'border-red-600/30 bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300',
}

const orderTones: Record<PurchaseOrderStatus, string> = {
  Draft: tone.neutral,
  PendingApproval: tone.amber,
  Ordered: tone.blue,
  PartlyReceived: tone.violet,
  Received: tone.green,
  Closed: tone.neutral,
  Cancelled: tone.red,
}

export function PurchaseOrderStatusBadge({ status, late }: { status: PurchaseOrderStatus; late?: boolean }) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <Badge variant="outline" className={orderTones[status]}>
        {t(purchaseOrderStatusLabels[status])}
      </Badge>
      {late && (
        <Badge variant="outline" className={tone.red}>
          {t('Late')}
        </Badge>
      )}
    </span>
  )
}

const billTones: Record<SupplierBillStatus, string> = {
  Unpaid: tone.amber,
  PartiallyPaid: tone.blue,
  Paid: tone.green,
  Cancelled: tone.red,
}

export function BillStatusBadge({ status, daysOverdue }: { status: SupplierBillStatus; daysOverdue?: number | null }) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <Badge variant="outline" className={billTones[status]}>
        {t(billStatusLabels[status])}
      </Badge>
      {daysOverdue != null && (
        <Badge variant="outline" className={tone.red}>
          {t('{days}d overdue', { days: daysOverdue })}
        </Badge>
      )}
    </span>
  )
}

const matchTones: Record<BillMatchStatus, string> = {
  Matched: tone.green,
  NeedsApproval: tone.amber,
  Approved: tone.violet,
}

export function MatchBadge({ status }: { status: BillMatchStatus }) {
  return (
    <Badge variant="outline" className={matchTones[status]}>
      {t(matchStatusLabels[status])}
    </Badge>
  )
}
