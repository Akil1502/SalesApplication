import { Bar, BarChart, CartesianGrid, ErrorBar, Rectangle, Tooltip, XAxis, YAxis, type BarShapeProps, type TooltipContentProps } from 'recharts'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { formatINR, formatINRCompact } from '@/lib/format'
import type { InsightMetrics } from './api'
import { forecastBars, type ForecastBar } from './insight-format'

function ForecastTooltip({ active, payload }: TooltipContentProps) {
  const bar = active ? (payload?.[0]?.payload as ForecastBar | undefined) : undefined
  if (!bar) return null
  return (
    <div className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-md">
      <p className="text-sm font-semibold tabular-nums">{formatINR(bar.value, 0)}</p>
      <p className="text-muted-foreground">
        {bar.month}
        {bar.forecast ? ' forecast' : ''}
      </p>
      {bar.forecast && (
        <>
          <p className="mt-1 tabular-nums">
            Likely range {formatINRCompact(bar.low)} to {formatINRCompact(bar.high)}
          </p>
          {bar.soFar !== undefined && <p className="tabular-nums">So far this month {formatINR(bar.soFar, 0)}</p>}
          {bar.lastYear != null && <p className="tabular-nums text-muted-foreground">Same month last year {formatINR(bar.lastYear, 0)}</p>}
        </>
      )}
    </div>
  )
}

/** Forecast months are lighter and outlined, with whiskers for the likely range, so they aren't read as actual sales. */
function ForecastShape(props: BarShapeProps) {
  const forecast = (props.payload as ForecastBar | undefined)?.forecast
  return forecast ? (
    <Rectangle {...props} fillOpacity={0.3} stroke="var(--viz-series-1)" strokeDasharray="4 3" strokeWidth={1.5} />
  ) : (
    <Rectangle {...props} />
  )
}

export function ForecastChart({ forecast }: { forecast: InsightMetrics['forecast'] }) {
  const bars = forecastBars(forecast)
  const level = useMotionLevel()
  if (bars.length === 0) return null
  const tick = { fill: 'var(--muted-foreground)', fontSize: 12 }
  const first = bars[0]!
  const last = bars.at(-1)!

  return (
    <figure aria-label={`Monthly sales from ${first.month} to ${last.month}, with the next ${forecast.next.length} months forecast. Exact values are in the tooltips.`}>
      <BarChart data={bars} responsive style={{ width: '100%', height: 280 }} margin={{ top: 8, right: 8, bottom: 0, left: 0 }} barCategoryGap="18%">
        <CartesianGrid vertical={false} stroke="var(--border)" />
        <XAxis dataKey="tick" tickLine={false} axisLine={{ stroke: 'var(--border)' }} tick={tick} interval="preserveStartEnd" minTickGap={12} />
        <YAxis tickFormatter={(v: number) => formatINRCompact(v)} tickLine={false} axisLine={false} tick={tick} width={72} />
        <Tooltip content={ForecastTooltip} cursor={{ fill: 'var(--muted)', opacity: 0.7 }} isAnimationActive={false} />
        <Bar dataKey="value" fill="var(--viz-series-1)" radius={[3, 3, 0, 0]} shape={ForecastShape} isAnimationActive={level !== 'off'} animationDuration={900}>
          <ErrorBar dataKey="range" width={6} stroke="var(--foreground)" strokeOpacity={0.55} />
        </Bar>
      </BarChart>
    </figure>
  )
}
