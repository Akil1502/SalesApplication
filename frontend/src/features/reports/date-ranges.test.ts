import { describe, expect, it } from 'vitest'
import { presetRange } from './date-ranges'

describe('presetRange', () => {
  const today = '2026-10-13'

  it('covers the month and financial year to date', () => {
    expect(presetRange('this-month', today)).toEqual({ from: '2026-10-01', to: today })
    expect(presetRange('this-fy', today)).toEqual({ from: '2026-04-01', to: today })
  })

  it('covers whole previous periods', () => {
    expect(presetRange('last-month', today)).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(presetRange('last-fy', today)).toEqual({ from: '2025-04-01', to: '2026-03-31' })
  })

  it('starts the last 12 months on the first of the month', () => {
    expect(presetRange('last-12-months', today)).toEqual({ from: '2025-11-01', to: today })
  })

  it('treats January to March as the tail of the financial year that began the April before', () => {
    expect(presetRange('this-fy', '2027-02-10')).toEqual({ from: '2026-04-01', to: '2027-02-10' })
    expect(presetRange('last-fy', '2027-02-10')).toEqual({ from: '2025-04-01', to: '2026-03-31' })
  })

  it('crosses year boundaries cleanly', () => {
    expect(presetRange('last-month', '2027-01-05')).toEqual({ from: '2026-12-01', to: '2026-12-31' })
    expect(presetRange('last-month', '2028-03-15')).toEqual({ from: '2028-02-01', to: '2028-02-29' })
  })
})
