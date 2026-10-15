import { ArrowDownRight, ArrowUpRight, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Card, CardContent, CardDescription, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'

const deltaFormatter = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 1, signDisplay: 'exceptZero', style: 'percent' })

/** Signed change with an arrow, so direction never relies on colour alone. Up is good for every tile that uses it. */
function Delta({ value, comparison }: { value: number | null; comparison: string }) {
  if (value === null) return <p className="text-xs text-muted-foreground">No sales {comparison} to compare</p>
  const up = value >= 0
  const Icon = up ? ArrowUpRight : ArrowDownRight
  return (
    <p className="flex items-center gap-1 text-xs">
      <span className={cn('inline-flex items-center gap-0.5 font-medium', up ? 'text-viz-up' : 'text-viz-down')}>
        <Icon className="size-3.5" aria-hidden />
        {deltaFormatter.format(value)}
      </span>
      <span className="text-muted-foreground">vs {comparison}</span>
    </p>
  )
}

interface StatTileProps {
  label: string
  icon: LucideIcon
  value: string | undefined
  isLoading: boolean
  delta?: { value: number | null; comparison: string }
  hint?: ReactNode
  /** Where the tile leads for detail. */
  to?: string
}

export function StatTile({ label, icon: Icon, value, isLoading, delta, hint, to }: StatTileProps) {
  const body = (
    <Card className={cn('h-full gap-2 py-4', to && 'transition-colors hover:bg-muted/40')}>
      <CardHeader className="flex flex-row items-center justify-between px-4">
        <CardDescription>{label}</CardDescription>
        <Icon className="size-4 text-muted-foreground" aria-hidden />
      </CardHeader>
      <CardContent className="space-y-1 px-4">
        {isLoading ? (
          <>
            <Skeleton className="h-8 w-28" />
            <Skeleton className="h-4 w-36" />
          </>
        ) : (
          <>
            <p className="text-2xl font-semibold tracking-tight">{value}</p>
            {delta && <Delta {...delta} />}
            {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
          </>
        )}
      </CardContent>
    </Card>
  )

  return to ? (
    <Link to={to} className="rounded-xl focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
      {body}
    </Link>
  ) : (
    body
  )
}
