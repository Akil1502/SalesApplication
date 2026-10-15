import type { Anomaly, AnomalyKind, ForecastMethod, InsightMetrics } from './api'

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

export const methodLabels: Record<ForecastMethod, string> = {
  None: 'Not enough history to forecast',
  RecentAverage: 'Average of the last three months',
  SeasonalNaive: 'Same month last year, adjusted for growth',
  HoltWintersAdditive: 'Holt-Winters (seasonal swings as fixed amounts)',
  HoltWintersMultiplicative: 'Holt-Winters (seasonal swings as a share of sales)',
}

/** How the forecast was chosen, in a sentence a manager can check. */
export function forecastExplanation(forecast: InsightMetrics['forecast']): string {
  const chosen = forecast.backtests.find((b) => b.method === forecast.method)
  if (forecast.method === 'None') return 'There are fewer than three months of sales, so there is no forecast yet.'
  if (!chosen) {
    return `${methodLabels[forecast.method]}. There isn't enough history yet to test it against past months, so treat the range as a rough guide.`
  }

  const others = forecast.backtests.filter((b) => b.method !== forecast.method)
  const compared = others.length
    ? ` It beat ${others.map((b) => `${methodLabels[b.method].toLowerCase()} (${b.meanAbsolutePercentError}%)`).join(' and ')}.`
    : ' Holt-Winters joins the comparison once there are two full years of history before the test months.'
  return `${methodLabels[forecast.method]}: forecasting the last ${forecast.holdoutMonths} months from the months before them, it was off by ${chosen.meanAbsolutePercentError}% on average.${compared}`
}

export interface ForecastBar {
  tick: string
  month: string
  value: number
  forecast: boolean
  /** Distance to the low and high ends of the range, for the whiskers on forecast bars. */
  range?: [number, number]
  low?: number
  high?: number
  soFar?: number
  lastYear?: number | null
}

/** "2026-09-01" as "Sep 2026". */
export function monthYear(isoDate: string): string {
  return monthLabels(isoDate).month
}

function monthLabels(isoDate: string) {
  const [year, month] = isoDate.split('-').map(Number) as [number, number]
  return { tick: `${MONTHS[month - 1]} ’${String(year % 100).padStart(2, '0')}`, month: `${MONTHS[month - 1]} ${year}` }
}

/** Full months as solid bars, then the forecast months with their range; the current month carries its total so far. */
export function forecastBars(forecast: InsightMetrics['forecast']): ForecastBar[] {
  const history = forecast.history.map((h) => ({ ...monthLabels(h.month), value: h.sales, forecast: false }))
  const next = forecast.next.map((n, i) => ({
    ...monthLabels(n.month),
    value: n.forecast,
    forecast: true,
    range: [n.forecast - n.low, n.high - n.forecast] as [number, number],
    low: n.low,
    high: n.high,
    soFar: i === 0 ? forecast.monthToDate : undefined,
    lastYear: n.sameMonthLastYear ?? null,
  }))
  return [...history, ...next]
}

export const anomalyLabels: Record<AnomalyKind, string> = {
  StoppedBuying: 'Stopped buying',
  UnusualInvoice: 'Unusual invoice',
  DemandSpike: 'Demand spike',
  DemandDrop: 'Demand drop',
  Stockout: 'Stock-out',
  PaymentRisk: 'Payment risk',
}

/** Where to look into an anomaly in the app. */
export function anomalyLink(anomaly: Anomaly): { to: string; label: string } | null {
  if (anomaly.reference) return { to: `/invoices?q=${encodeURIComponent(anomaly.reference)}`, label: 'Open invoice' }
  if (anomaly.customerId) return { to: `/customers/${anomaly.customerId}`, label: 'Open customer' }
  if (anomaly.productId) return { to: `/products/${anomaly.productId}`, label: 'Open product' }
  return null
}

/** Days of stock left, as people say it. */
export function coverText(days: number): string {
  if (days <= 0) return 'Out of stock'
  if (days >= 999) return 'No recent sales'
  return days < 1 ? 'Under a day' : `${Math.round(days)} ${Math.round(days) === 1 ? 'day' : 'days'}`
}
