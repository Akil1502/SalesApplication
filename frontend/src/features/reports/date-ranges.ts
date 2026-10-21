import { msg } from '@/lib/i18n'

export type RangePreset = 'this-month' | 'last-month' | 'this-fy' | 'last-fy' | 'last-12-months' | 'custom'

export const rangePresets: { value: RangePreset; label: string }[] = [
  { value: 'this-month', label: msg('This month') },
  { value: 'last-month', label: msg('Last month') },
  { value: 'this-fy', label: msg('This financial year') },
  { value: 'last-fy', label: msg('Last financial year') },
  { value: 'last-12-months', label: msg('Last 12 months') },
  { value: 'custom', label: msg('Custom range') },
]

export interface DateRange {
  from: string
  to: string
}

/** "YYYY-MM-DD" for a UTC calendar date, so no time zone can shift the day. */
const iso = (year: number, monthIndex: number, day: number) => new Date(Date.UTC(year, monthIndex, day)).toISOString().slice(0, 10)

/**
 * The dates a preset covers, as of `today` (India's date, "YYYY-MM-DD"). Financial years run April to March;
 * ranges that include the current period end today rather than at the period end.
 */
export function presetRange(preset: Exclude<RangePreset, 'custom'>, today: string): DateRange {
  const [year, month] = today.split('-').map(Number) as [number, number]
  const monthIndex = month - 1
  const fyStartYear = month >= 4 ? year : year - 1

  switch (preset) {
    case 'this-month':
      return { from: iso(year, monthIndex, 1), to: today }
    case 'last-month':
      // Day 0 of a month is the last day of the month before.
      return { from: iso(year, monthIndex - 1, 1), to: iso(year, monthIndex, 0) }
    case 'this-fy':
      return { from: iso(fyStartYear, 3, 1), to: today }
    case 'last-fy':
      return { from: iso(fyStartYear - 1, 3, 1), to: iso(fyStartYear, 2, 31) }
    case 'last-12-months':
      return { from: iso(year, monthIndex - 11, 1), to: today }
  }
}
