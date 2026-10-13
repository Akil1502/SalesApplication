import { describe, expect, it } from 'vitest'
import { formatBusinessDate, formatINR, formatINRCompact, formatPercent, formatQuantity, todayInIndia } from './format'

describe('business dates', () => {
  it('formats a DateOnly without shifting the day', () => {
    expect(formatBusinessDate('2026-04-01')).toBe('01 Apr 2026')
    expect(formatBusinessDate(null)).toBe('—')
  })

  it("uses India's date, which is ahead of UTC", () => {
    // 20:00 UTC on 31 March is already 1 April in India.
    expect(todayInIndia(new Date('2027-03-31T20:00:00Z'))).toBe('2027-04-01')
    expect(todayInIndia(new Date('2027-03-31T10:00:00Z'))).toBe('2027-03-31')
  })
})

describe('formatINR', () => {
  it('uses lakh and crore grouping', () => {
    expect(formatINR(1234567.5)).toBe('₹12,34,567.50')
    expect(formatINR(125000000)).toBe('₹12,50,00,000.00')
  })

  it('supports whole-rupee display', () => {
    expect(formatINR(1999.6, 0)).toBe('₹2,000')
  })

  it('shows a dash for missing values', () => {
    expect(formatINR(null)).toBe('—')
    expect(formatINR(undefined)).toBe('—')
  })
})

describe('formatQuantity', () => {
  it('keeps up to three decimals and appends the unit', () => {
    expect(formatQuantity(1250.5, 'Kg')).toBe('1,250.5 Kg')
    expect(formatQuantity(0.125)).toBe('0.125')
  })
})

describe('formatPercent', () => {
  it('drops trailing zeros', () => {
    expect(formatPercent(5)).toBe('5%')
    expect(formatPercent(0.25)).toBe('0.25%')
  })
})

describe('formatINRCompact', () => {
  it('uses crore and lakh for large amounts', () => {
    expect(formatINRCompact(45230000)).toBe('₹4.52 Cr')
    expect(formatINRCompact(1234567)).toBe('₹12.35 L')
    expect(formatINRCompact(100000)).toBe('₹1 L')
  })

  it('shows smaller amounts in full, and keeps the sign', () => {
    expect(formatINRCompact(99999.4)).toBe('₹99,999')
    expect(formatINRCompact(-2500000)).toBe('-₹25 L')
    expect(formatINRCompact(null)).toBe('—')
  })
})
