import { Bar, BarChart, CartesianGrid, Line, LineChart, Tooltip, XAxis, YAxis, type TooltipContentProps } from 'recharts'
import { formatINRCompact } from '@/lib/format'
import type { AskAnswer, AskColumn } from './api'
import { formatAskValue, toNumber } from './ask-format'

const MAX_POINTS = 30
const countFormatter = new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 })

interface Point {
  label: string
  value: number
}

function axisValue(value: number, column: AskColumn) {
  return column.kind === 'Money' ? formatINRCompact(value) : countFormatter.format(value)
}

function ChartTooltip({ active, payload, column }: TooltipContentProps & { column: AskColumn }) {
  const point = active ? (payload?.[0]?.payload as Point | undefined) : undefined
  if (!point) return null
  return (
    <div className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-md">
      <p className="text-sm font-semibold tabular-nums">
        {column.kind === 'Money' ? '₹' : ''}
        {formatAskValue(point.value, column)}
      </p>
      <p className="text-muted-foreground">{point.label}</p>
    </div>
  )
}

/**
 * One series, in the single-series colour: columns or bars to compare categories, a line for a trend. Long
 * category lists switch to horizontal bars so the names stay readable. The table below always has every value.
 */
export function AnswerChart({ answer }: { answer: AskAnswer }) {
  const xIndex = answer.columns.findIndex((c) => c.name === answer.chart.x)
  const yIndex = answer.columns.findIndex((c) => c.name === answer.chart.y)
  if (answer.chart.type === 'None' || xIndex < 0 || yIndex < 0) return null

  const xColumn = answer.columns[xIndex]!
  const yColumn = answer.columns[yIndex]!
  const data: Point[] = answer.rows
    .slice(0, MAX_POINTS)
    .map((row) => ({ label: formatAskValue(row[xIndex], xColumn) || '(blank)', value: toNumber(row[yIndex]) ?? 0 }))
  if (data.length < 2) return null

  const tick = { fill: 'var(--muted-foreground)', fontSize: 12 }
  const tooltip = (props: TooltipContentProps) => <ChartTooltip {...props} column={yColumn} />
  const note = answer.rows.length > MAX_POINTS ? `The chart shows the first ${MAX_POINTS} of ${answer.rows.length} rows.` : null

  if (answer.chart.type === 'Line') {
    return (
      <figure className="space-y-1" aria-label={`${yColumn.name} by ${xColumn.name}, line chart. Exact values are in the table.`}>
        <LineChart data={data} responsive style={{ width: '100%', height: 260 }} margin={{ top: 8, right: 12, bottom: 0, left: 0 }}>
          <CartesianGrid vertical={false} stroke="var(--border)" />
          <XAxis dataKey="label" tickLine={false} axisLine={{ stroke: 'var(--border)' }} tick={tick} interval="preserveStartEnd" minTickGap={16} />
          <YAxis tickFormatter={(v: number) => axisValue(v, yColumn)} tickLine={false} axisLine={false} tick={tick} width={72} />
          <Tooltip content={tooltip} cursor={{ stroke: 'var(--border)' }} isAnimationActive={false} />
          <Line dataKey="value" stroke="var(--viz-series-1)" strokeWidth={2} dot={false}
            activeDot={{ r: 5, fill: 'var(--viz-series-1)', stroke: 'var(--card)', strokeWidth: 2 }} isAnimationActive={false} />
        </LineChart>
        {note && <figcaption className="text-xs text-muted-foreground">{note}</figcaption>}
      </figure>
    )
  }

  const horizontal = data.length > 8 || data.some((d) => d.label.length > 14)
  return (
    <figure className="space-y-1" aria-label={`${yColumn.name} by ${xColumn.name}, bar chart. Exact values are in the table.`}>
      {horizontal ? (
        <BarChart data={data} layout="vertical" responsive style={{ width: '100%', height: data.length * 30 + 40 }}
          margin={{ top: 4, right: 16, bottom: 0, left: 0 }} barCategoryGap="22%">
          <CartesianGrid horizontal={false} stroke="var(--border)" />
          <XAxis type="number" tickFormatter={(v: number) => axisValue(v, yColumn)} tickLine={false} axisLine={false} tick={tick} />
          <YAxis type="category" dataKey="label" tickLine={false} axisLine={{ stroke: 'var(--border)' }} tick={tick} width={170} interval={0} />
          <Tooltip content={tooltip} cursor={{ fill: 'var(--muted)', opacity: 0.7 }} isAnimationActive={false} />
          <Bar dataKey="value" fill="var(--viz-series-1)" radius={[0, 4, 4, 0]} maxBarSize={20} isAnimationActive={false} />
        </BarChart>
      ) : (
        <BarChart data={data} responsive style={{ width: '100%', height: 260 }} margin={{ top: 8, right: 12, bottom: 0, left: 0 }} barCategoryGap="22%">
          <CartesianGrid vertical={false} stroke="var(--border)" />
          <XAxis dataKey="label" tickLine={false} axisLine={{ stroke: 'var(--border)' }} tick={tick} interval={0} />
          <YAxis tickFormatter={(v: number) => axisValue(v, yColumn)} tickLine={false} axisLine={false} tick={tick} width={72} />
          <Tooltip content={tooltip} cursor={{ fill: 'var(--muted)', opacity: 0.7 }} isAnimationActive={false} />
          <Bar dataKey="value" fill="var(--viz-series-1)" radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false} />
        </BarChart>
      )}
      {note && <figcaption className="text-xs text-muted-foreground">{note}</figcaption>}
    </figure>
  )
}
