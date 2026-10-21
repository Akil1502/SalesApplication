import { Badge } from '@/components/ui/badge'
import { msg, t } from '@/lib/i18n'
import { cn } from '@/lib/utils'
import { movementTypeLabels, type StockMovementType, type StockStatus } from './api'

const statusStyles: Record<StockStatus, { label: string; className: string }> = {
  InStock: { label: msg('In stock'), className: 'border-emerald-600/30 bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300' },
  Low: { label: msg('Low'), className: 'border-amber-600/30 bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-300' },
  OutOfStock: { label: msg('Out of stock'), className: 'border-red-600/30 bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300' },
}

export function StockStatusBadge({ status }: { status: StockStatus }) {
  const style = statusStyles[status]
  return (
    <Badge variant="outline" className={style.className}>
      {t(style.label)}
    </Badge>
  )
}

export function MovementTypeBadge({ type }: { type: StockMovementType }) {
  return (
    <Badge variant={type === 'Adjustment' ? 'outline' : 'secondary'} className={cn(type === 'Sale' && 'bg-sky-50 text-sky-800 dark:bg-sky-950 dark:text-sky-300')}>
      {t(movementTypeLabels[type])}
    </Badge>
  )
}

/** Signed quantity: green for stock in, red for stock out. */
export function SignedQuantity({ quantity, unit }: { quantity: number; unit: string }) {
  const formatted = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 3, signDisplay: 'always' }).format(quantity)
  return (
    <span className={cn('tabular-nums', quantity > 0 ? 'text-emerald-700 dark:text-emerald-400' : 'text-red-700 dark:text-red-400')}>
      {formatted} {unit}
    </span>
  )
}
