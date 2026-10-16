import { describe, expect, it } from 'vitest'
import { applyPercent, monthPeriod, parseRate } from './rate-math'

describe('rate helpers', () => {
  it('applies a percentage and rounds to the paisa or the rupee', () => {
    expect(applyPercent(300, 5, 'paisa')).toBe('315.00')
    expect(applyPercent(312.4, 2.5, 'paisa')).toBe('320.21')
    expect(applyPercent(312.4, 2.5, 'rupee')).toBe('320.00')
    expect(applyPercent(100, -10, 'rupee')).toBe('90.00')
    expect(applyPercent(10, -150, 'paisa')).toBe('0.00')
  })

  it('reads typed rates, allowing ₹ and commas, at most two decimals', () => {
    expect(parseRate('315')).toBe(315)
    expect(parseRate('₹1,250.50')).toBe(1250.5)
    expect(parseRate('12.345')).toBeNull()
    expect(parseRate('-5')).toBeNull()
    expect(parseRate('abc')).toBeNull()
    expect(parseRate('')).toBeNull()
  })

  it('gives whole months, including February and the year end', () => {
    expect(monthPeriod('2026-01-15')).toEqual({ from: '2026-01-01', to: '2026-01-31' })
    expect(monthPeriod('2026-01-31', 1)).toEqual({ from: '2026-02-01', to: '2026-02-28' })
    expect(monthPeriod('2028-02-10')).toEqual({ from: '2028-02-01', to: '2028-02-29' })
    expect(monthPeriod('2026-12-05', 1)).toEqual({ from: '2027-01-01', to: '2027-01-31' })
  })
})
