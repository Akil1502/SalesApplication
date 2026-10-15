import { describe, expect, it } from 'vitest'
import { columnHeader, formatCell, isNumeric } from './report-format'

describe('report cells', () => {
  it('formats money with Indian grouping and no currency sign', () => {
    expect(formatCell(1234567.5, 'Money')).toBe('12,34,567.50')
    expect(columnHeader({ key: 'taxable', header: 'Taxable value', type: 'Money' })).toBe('Taxable value (₹)')
  })

  it('turns fractions into percentages', () => {
    expect(formatCell(0.125, 'Percent')).toBe('12.5%')
    expect(formatCell(0.05, 'Percent')).toBe('5%')
  })

  it('formats quantities, counts and dates', () => {
    expect(formatCell(1500.125, 'Quantity')).toBe('1,500.125')
    expect(formatCell(123456, 'Count')).toBe('1,23,456')
    expect(formatCell('2026-04-01', 'Date')).toBe('01 Apr 2026')
  })

  it('leaves empty cells blank and passes text through', () => {
    expect(formatCell(null, 'Money')).toBe('')
    expect(formatCell(undefined, 'Text')).toBe('')
    expect(formatCell('Total', 'Text')).toBe('Total')
    expect(formatCell('Total', 'Date')).toBe('Total')
  })

  it('right-aligns only numeric columns', () => {
    expect(isNumeric('Money')).toBe(true)
    expect(isNumeric('Count')).toBe(true)
    expect(isNumeric('Text')).toBe(false)
    expect(isNumeric('Date')).toBe(false)
  })
})
