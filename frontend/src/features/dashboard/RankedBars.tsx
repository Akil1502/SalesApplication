import { formatINRCompact } from '@/lib/format'

export interface RankedBar {
  label: string
  value: number
  /** Fraction of the whole (0.25 = 25%), shown beside the bar. */
  share?: number
  detail?: string | null
  /** Bar colour; defaults to the single-series colour. */
  color?: string
}

const shareFormatter = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 1 })

/**
 * A short ranked list drawn as thin horizontal bars. Every value is printed beside its bar, so the list doubles
 * as its own table and nothing depends on reading bar lengths or colours.
 */
export function RankedBars({ items, emptyText }: { items: RankedBar[]; emptyText: string }) {
  if (items.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">{emptyText}</p>
  const max = Math.max(...items.map((i) => i.value), 0)

  return (
    <ul className="flex flex-col gap-3">
      {items.map((item) => (
        <li key={item.label} className="-mx-2 rounded-md px-2 py-1 transition-colors hover:bg-muted/50">
          <div className="flex items-baseline justify-between gap-3 text-sm">
            <span className="min-w-0 truncate font-medium" title={item.label}>
              {item.label}
            </span>
            <span className="shrink-0 tabular-nums">{formatINRCompact(item.value)}</span>
          </div>
          <div className="mt-1.5 flex items-center gap-3">
            <div className="h-2 flex-1">
              <div
                className="h-full rounded-r-[4px]"
                style={{
                  width: `${max > 0 ? Math.max((item.value / max) * 100, item.value > 0 ? 1 : 0) : 0}%`,
                  background: item.color ?? 'var(--viz-series-1)',
                }}
              />
            </div>
            {item.share !== undefined && (
              <span className="w-12 shrink-0 text-right text-xs text-muted-foreground tabular-nums">{shareFormatter.format(item.share)}</span>
            )}
          </div>
          {item.detail && <p className="mt-0.5 truncate text-xs text-muted-foreground">{item.detail}</p>}
        </li>
      ))}
    </ul>
  )
}
