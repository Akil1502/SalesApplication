import { ArrowDownRight, ArrowUpRight, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { CountUp } from '@/components/fx/CountUp'
import { TiltCard } from '@/components/fx/TiltCard'
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
  /** The number behind the value, to count up to; the value is shown as is when left out. */
  count?: { value: number; format: (value: number) => string }
  isLoading: boolean
  delta?: { value: number | null; comparison: string }
  hint?: ReactNode
  /** Where the tile leads for detail. */
  to?: string
}

export function StatTile({ label, icon: Icon, value, count, isLoading, delta, hint, to }: StatTileProps) {
  const body = (
    <Card className="relative h-full gap-2 py-4">
      {/* A soft wash of brand colour in the corner, behind the icon. */}
      <span
        aria-hidden
        className="pointer-events-none absolute -top-10 -right-10 size-28 rounded-full opacity-20 blur-2xl dark:opacity-30"
        style={{ background: 'var(--gradient-brand)' }}
      />
      <CardHeader className="flex flex-row items-center justify-between px-4">
        <CardDescription>{label}</CardDescription>
        <span
          className="flex size-8 items-center justify-center rounded-lg text-white shadow-[0_4px_12px_-4px_var(--brand-glow)]"
          style={{ background: 'var(--gradient-brand)' }}
        >
          <Icon className="size-4" aria-hidden />
        </span>
      </CardHeader>
      <CardContent className="space-y-1 px-4">
        {isLoading ? (
          <>
            <Skeleton className="h-8 w-28" />
            <Skeleton className="h-4 w-36" />
          </>
        ) : (
          <>
            <p className="text-2xl font-semibold tracking-tight">{count ? <CountUp value={count.value} format={count.format} /> : value}</p>
            {delta && <Delta {...delta} />}
            {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
          </>
        )}
      </CardContent>
    </Card>
  )

  return (
    <TiltCard className="h-full rounded-xl">
      {to ? (
        <Link to={to} className="block h-full rounded-xl focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
          {body}
        </Link>
      ) : (
        body
      )}
    </TiltCard>
  )
}
