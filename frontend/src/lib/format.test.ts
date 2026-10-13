import { describe, expect, it } from 'vitest'
import { formatINR, formatPercent, formatQuantity } from './format'

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
