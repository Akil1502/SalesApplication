import type { Schemas } from '@/lib/api/client'
import { formatDateParts } from '@/lib/format'
import { getLanguage } from '@/lib/i18n'

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

/** "Oct ’26" and "Oct 2026" for a month (yyyy-mm…), with the month named in the language on screen. */
export function monthLabels(isoMonth: string): { tick: string; month: string } {
  const [year, month] = isoMonth.split('-').map(Number) as [number, number]
  const name = getLanguage() === 'en' ? MONTHS[month - 1] : formatDateParts(new Date(Date.UTC(year, month - 1, 1)), { month: 'short', timeZone: 'UTC' })
  return { tick: `${name} ’${String(year % 100).padStart(2, '0')}`, month: `${name} ${year}` }
}

/** A month of sales as the charts show it; currentMonth is yyyy-mm. */
export function toTrendPoint(m: MonthlySales, currentMonth: string): TrendPoint {
  return {
    ...monthLabels(m.month),
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
