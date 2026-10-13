import { describe, expect, it } from 'vitest'
import { change } from './kpi'

describe('change', () => {
  it('is the fractional change from the earlier period', () => {
    expect(change(120, 100)).toBeCloseTo(0.2)
    expect(change(75, 100)).toBeCloseTo(-0.25)
  })

  it('has nothing to compare against when the earlier period had no sales', () => {
    expect(change(500, 0)).toBeNull()
  })
})
