import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, Rectangle, Tooltip, XAxis, YAxis, type BarShapeProps, type TooltipContentProps } from 'recharts'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatINR, formatINRCompact, todayInIndia } from '@/lib/format'
import { toTrendPoint, type MonthlySales, type TrendPoint } from './trend'

function TrendTooltip({ active, payload }: TooltipContentProps) {
  const point = active ? (payload?.[0]?.payload as TrendPoint | undefined) : undefined
  if (!point) return null
  return (
    <div className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-md">
      <p className="text-sm font-semibold tabular-nums">{formatINR(point.value, 0)}</p>
      <p className="text-muted-foreground">
        {point.month}
        {point.partial && ' so far'} · {point.invoices.toLocaleString('en-IN')} invoices
      </p>
    </div>
  )
}

/** The current, unfinished month is drawn lighter so it isn't read as a slump. */
function TrendBar(props: BarShapeProps) {
  const partial = (props.payload as TrendPoint | undefined)?.partial
  return <Rectangle {...props} fillOpacity={partial ? 0.45 : 1} />
}

/** Monthly taxable sales for the last 24 months, as columns with a table view for exact figures. */
export function SalesTrendChart({ months }: { months: MonthlySales[] }) {
  const level = useMotionLevel()
  const [view, setView] = useState<'chart' | 'table'>('chart')
  const currentMonth = todayInIndia().slice(0, 7)
  const data = months.map((m) => toTrendPoint(m, currentMonth))
  const best = data.reduce<TrendPoint | undefined>((top, p) => (!top || p.value > top.value ? p : top), undefined)

  const flatChart = (
    <div role="img" aria-label={`Monthly sales for the last ${data.length} months${best ? `, highest in ${best.month}` : ''}. Switch to the table for exact figures.`}>
      <BarChart data={data} responsive style={{ width: '100%', height: 280 }} margin={{ top: 8, right: 4, bottom: 0, left: 0 }} barCategoryGap="18%">
        <defs>
          <linearGradient id="trend-bar" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" stopColor="var(--brand-2)" />
            <stop offset="1" stopColor="var(--viz-series-1)" />
          </linearGradient>
        </defs>
        <CartesianGrid vertical={false} stroke="var(--border)" />
        <XAxis
          dataKey="tick"
          tickLine={false}
          axisLine={{ stroke: 'var(--border)' }}
          tick={{ fill: 'var(--muted-foreground)', fontSize: 12 }}
          interval="preserveStartEnd"
          minTickGap={12}
        />
        <YAxis tickFormatter={(v: number) => formatINRCompact(v)} tickLine={false} axisLine={false} tick={{ fill: 'var(--muted-foreground)', fontSize: 12 }} width={72} />
        <Tooltip content={TrendTooltip} cursor={{ fill: 'var(--muted)', opacity: 0.7 }} isAnimationActive={false} />
        <Bar
          dataKey="value"
          fill="url(#trend-bar)"
          radius={[4, 4, 0, 0]}
          maxBarSize={24}
          shape={TrendBar}
          isAnimationActive={level !== 'off'}
          animationDuration={level === 'full' ? 1100 : 400}
          animationEasing="ease-out"
        />
      </BarChart>
    </div>
  )

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">
          {best && `Best month: ${best.month}, ${formatINRCompact(best.value)}. `}
          {data.some((p) => p.partial) && 'The lighter column is this month so far.'}
        </p>
        <div className="flex gap-1" role="group" aria-label="Show as">
          <Button size="sm" variant={view === 'chart' ? 'secondary' : 'ghost'} onClick={() => setView('chart')} aria-pressed={view === 'chart'}>
            Chart
          </Button>
          <Button size="sm" variant={view === 'table' ? 'secondary' : 'ghost'} onClick={() => setView('table')} aria-pressed={view === 'table'}>
            Table
          </Button>
        </div>
      </div>

      {view === 'chart' ? (
        flatChart
      ) : (
        <div className="max-h-80 overflow-y-auto rounded-lg border">
          <Table>
            <TableHeader className="sticky top-0 bg-muted">
              <TableRow>
                <TableHead>Month</TableHead>
                <TableHead className="text-right">Invoices</TableHead>
                <TableHead className="text-right">Sales (excl. GST)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {[...data].reverse().map((p) => (
                <TableRow key={p.month}>
                  <TableCell>
                    {p.month}
                    {p.partial && <span className="text-muted-foreground"> (so far)</span>}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{p.invoices.toLocaleString('en-IN')}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatINR(p.value, 0)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  )
}
