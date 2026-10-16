import { describe, expect, it } from 'vitest'
import { gstDeadline } from './rules'

describe('GST deadline for credit notes and returns', () => {
  it('is 30 November after the financial year of the document', () => {
    expect(gstDeadline('2026-04-01')).toBe('2027-11-30')
    expect(gstDeadline('2026-10-16')).toBe('2027-11-30')
    expect(gstDeadline('2027-03-31')).toBe('2027-11-30')
    expect(gstDeadline('2027-04-01')).toBe('2028-11-30')
  })
})
