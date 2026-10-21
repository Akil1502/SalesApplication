import { monthLabels } from '@/features/dashboard/trend'
import { getLanguage, msg, t, tn } from '@/lib/i18n'
import type { Anomaly, AnomalyKind, ForecastMethod, InsightMetrics } from './api'

/** Marked here; translate with t() where shown. */
export const methodLabels: Record<ForecastMethod, string> = {
  None: msg('Not enough history to forecast'),
  RecentAverage: msg('Average of the last three months'),
  SeasonalNaive: msg('Same month last year, adjusted for growth'),
  HoltWintersAdditive: msg('Holt-Winters (seasonal swings as fixed amounts)'),
  HoltWintersMultiplicative: msg('Holt-Winters (seasonal swings as a share of sales)'),
}

const methodName = (method: ForecastMethod) => t(methodLabels[method])

/** How the forecast was chosen, in a sentence a manager can check. */
export function forecastExplanation(forecast: InsightMetrics['forecast']): string {
  const chosen = forecast.backtests.find((b) => b.method === forecast.method)
  if (forecast.method === 'None') return t('There are fewer than three months of sales, so there is no forecast yet.')
  if (!chosen) {
    return t("{method}. There isn't enough history yet to test it against past months, so treat the range as a rough guide.", { method: methodName(forecast.method) })
  }

  const others = forecast.backtests.filter((b) => b.method !== forecast.method)
  const language = getLanguage()
  const list = new Intl.ListFormat(language === 'ta' ? 'ta-IN' : 'en-IN', { type: 'conjunction' })
  const lower = (text: string) => (language === 'en' ? text.toLowerCase() : text)
  const beaten = others.map((b) => t('{method} ({error}%)', { method: lower(methodName(b.method)), error: b.meanAbsolutePercentError }))
  const compared = others.length
    ? t('It beat {methods}.', { methods: list.format(beaten) })
    : t('Holt-Winters joins the comparison once there are two full years of history before the test months.')
  const result = t('{method}: forecasting the last {months} months from the months before them, it was off by {error}% on average.', {
    method: methodName(forecast.method),
    months: forecast.holdoutMonths,
    error: chosen.meanAbsolutePercentError,
  })
  return `${result} ${compared}`
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

/** Marked here; translate with t() where shown. */
export const anomalyLabels: Record<AnomalyKind, string> = {
  StoppedBuying: msg('Stopped buying'),
  UnusualInvoice: msg('Unusual invoice'),
  DemandSpike: msg('Demand spike'),
  DemandDrop: msg('Demand drop'),
  Stockout: msg('Stock-out'),
  PaymentRisk: msg('Payment risk'),
  HighReturns: msg('Many returns'),
}

/** Where to look into an anomaly in the app. */
export function anomalyLink(anomaly: Anomaly): { to: string; label: string } | null {
  if (anomaly.reference) return { to: `/invoices?q=${encodeURIComponent(anomaly.reference)}`, label: t('Open invoice') }
  if (anomaly.kind === 'HighReturns' && anomaly.customerId) return { to: `/credit-notes?customerId=${anomaly.customerId}`, label: t('Their credit notes') }
  if (anomaly.customerId) return { to: `/customers/${anomaly.customerId}`, label: t('Open customer') }
  if (anomaly.productId) return { to: `/products/${anomaly.productId}`, label: t('Open product') }
  return null
}

/** Days of stock left, as people say it. */
export function coverText(days: number): string {
  if (days <= 0) return t('Out of stock')
  if (days >= 999) return t('No recent sales')
  return days < 1 ? t('Under a day') : tn(Math.round(days), '{count} day', '{count} days')
}
