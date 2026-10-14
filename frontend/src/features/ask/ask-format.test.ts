import { describe, expect, it } from 'vitest'
import { askColumnHeader, formatAskValue, isNumericColumn, toNumber } from './ask-format'

describe('answer formatting', () => {
  it('formats money like the reports, with ₹ once in the header', () => {
    const column = { name: 'SalesValue', label: 'Sales value', kind: 'Money' as const }
    expect(formatAskValue(1234567.5, column)).toBe('12,34,567.50')
    expect(askColumnHeader(column)).toBe('Sales value (₹)')
    expect(askColumnHeader({ name: 'SalesValue', kind: 'Money' })).toBe('SalesValue (₹)')
    expect(isNumericColumn(column)).toBe(true)
  })

  it('formats counts, numbers, dates and yes/no', () => {
    expect(formatAskValue(123456, { name: 'Invoices', kind: 'Count' })).toBe('1,23,456')
    expect(formatAskValue(1500.125, { name: 'Quantity', kind: 'Number' })).toBe('1,500.125')
    expect(formatAskValue('2026-04-01', { name: 'MonthStart', kind: 'Date' })).toBe('01 Apr 2026')
    expect(formatAskValue(true, { name: 'IsActive', kind: 'Boolean' })).toBe('Yes')
    expect(formatAskValue(null, { name: 'City', kind: 'Text' })).toBe('')
  })

  it('reads chart values', () => {
    expect(toNumber(12.5)).toBe(12.5)
    expect(toNumber('42')).toBe(42)
    expect(toNumber('Tiruppur')).toBeNull()
    expect(toNumber(null)).toBeNull()
  })
})
