import { describe, expect, it } from 'vitest'
import type { Anomaly, InsightMetrics } from './api'
import { anomalyLink, coverText, forecastBars, forecastExplanation } from './insight-format'

const forecast: InsightMetrics['forecast'] = {
  method: 'SeasonalNaive',
  backtests: [{ method: 'SeasonalNaive', meanAbsolutePercentError: 18.2 }],
  holdoutMonths: 6,
  history: [
    { month: '2026-08-01', sales: 43_500_000 },
    { month: '2026-09-01', sales: 42_900_000 },
  ],
  next: [
    { month: '2026-10-01', forecast: 56_600_000, low: 40_000_000, high: 73_200_000, sameMonthLastYear: 43_700_000 },
    { month: '2026-11-01', forecast: 45_300_000, low: 26_500_000, high: 64_000_000, sameMonthLastYear: null },
  ],
  monthToDate: 25_100_000,
}

describe('insights formatting', () => {
  it('explains how the forecast was chosen and why Holt-Winters is missing', () => {
    expect(forecastExplanation(forecast)).toBe(
      'Same month last year, adjusted for growth: forecasting the last 6 months from the months before them, it was off by 18.2% on average. Holt-Winters joins the comparison once there are two full years of history before the test months.',
    )
    const compared = { ...forecast, method: 'HoltWintersMultiplicative' as const, backtests: [...forecast.backtests, { method: 'HoltWintersMultiplicative' as const, meanAbsolutePercentError: 7.4 }] }
    expect(forecastExplanation(compared)).toContain('off by 7.4% on average. It beat same month last year, adjusted for growth (18.2%).')
    expect(forecastExplanation({ ...forecast, backtests: [] })).toContain("isn't enough history yet")
  })

  it('draws full months, then forecast months with their range and the month so far', () => {
    const bars = forecastBars(forecast)

    expect(bars.map((b) => b.tick)).toEqual(['Aug ’26', 'Sep ’26', 'Oct ’26', 'Nov ’26'])
    expect(bars.map((b) => b.forecast)).toEqual([false, false, true, true])
    expect(bars[2]).toMatchObject({ month: 'Oct 2026', value: 56_600_000, range: [16_600_000, 16_600_000], soFar: 25_100_000, lastYear: 43_700_000 })
    expect(bars[3]?.soFar).toBeUndefined()
  })

  it('links an anomaly to the invoice, customer or product it concerns', () => {
    const base = { kind: 'UnusualInvoice', title: '', detail: '', score: 1 } as Anomaly
    expect(anomalyLink({ ...base, reference: 'INV/26-27/00183', customerId: 4 })).toEqual({ to: '/invoices?q=INV%2F26-27%2F00183', label: 'Open invoice' })
    expect(anomalyLink({ ...base, customerId: 4 })).toEqual({ to: '/customers/4', label: 'Open customer' })
    expect(anomalyLink({ ...base, productId: 9 })).toEqual({ to: '/products/9', label: 'Open product' })
    expect(anomalyLink(base)).toBeNull()
  })

  it('describes days of stock left', () => {
    expect(coverText(0)).toBe('Out of stock')
    expect(coverText(0.7)).toBe('Under a day')
    expect(coverText(1)).toBe('1 day')
    expect(coverText(12.8)).toBe('13 days')
    expect(coverText(999)).toBe('No recent sales')
  })
})
