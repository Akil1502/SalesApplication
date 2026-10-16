/** How a percentage change is rounded: to the paisa, or to whole rupees as most rate lists are. */
export type Rounding = 'paisa' | 'rupee'

/** A price changed by a percentage (e.g. +5 or -2.5), rounded, as the text the rate box shows. */
export function applyPercent(price: number, percent: number, rounding: Rounding): string {
  const changed = price * (1 + percent / 100)
  const rounded = rounding === 'rupee' ? Math.round(changed) : Math.round(changed * 100) / 100
  return Math.max(0, rounded).toFixed(2)
}

/** A rate typed into a box: a non-negative number with at most two decimals, else null. */
export function parseRate(text: string): number | null {
  const trimmed = text.replace(/[₹,\s]/g, '')
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) return null
  const value = Number(trimmed)
  return value <= 99_999_999 ? value : null
}

const pad = (n: number) => String(n).padStart(2, '0')

/** The first and last day (yyyy-mm-dd) of the month `offset` months after the one containing `day`. */
export function monthPeriod(day: string, offset = 0): { from: string; to: string } {
  const [year, month] = day.split('-').map(Number) as [number, number]
  const first = new Date(Date.UTC(year, month - 1 + offset, 1))
  const last = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 0))
  const iso = (d: Date) => `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
  return { from: iso(first), to: iso(last) }
}
