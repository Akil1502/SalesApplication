import type { Schemas } from '@/lib/api/client'

export type MonthlySales = Schemas['MonthlySalesDto']

export interface TrendPoint {
  /** Short label, e.g. "Oct ’26". */
  tick: string
  /** Full label, e.g. "Oct 2026". */
  month: string
  value: number
  invoices: number
  /** The current month, still in progress. */
  partial: boolean
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** A month of sales as the charts show it; currentMonth is yyyy-mm. */
export function toTrendPoint(m: MonthlySales, currentMonth: string): TrendPoint {
  const [year, month] = m.month.split('-').map(Number) as [number, number]
  return {
    tick: `${MONTHS[month - 1]} ’${String(year % 100).padStart(2, '0')}`,
    month: `${MONTHS[month - 1]} ${year}`,
    value: m.taxableValue,
    invoices: m.invoices,
    partial: m.month.startsWith(currentMonth),
  }
}

/** Whether this month so far already beats every earlier month: worth a celebration. */
export function isRecordMonth(points: TrendPoint[]): boolean {
  const current = points.find((p) => p.partial)
  const earlier = points.filter((p) => !p.partial)
  return !!current && earlier.length >= 3 && current.value > Math.max(...earlier.map((p) => p.value))
}
