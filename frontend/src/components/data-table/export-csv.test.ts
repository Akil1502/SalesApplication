import { describe, expect, it } from 'vitest'
import { csvField, exportValue, toCsv } from './export-csv'
import { viewSearch } from './table-settings'

describe('list export', () => {
  it('quotes only what needs it', () => {
    expect(csvField('Pandian Knits')).toBe('Pandian Knits')
    expect(csvField('Kongu Yarn Agencies, Erode')).toBe('"Kongu Yarn Agencies, Erode"')
    expect(csvField('5" roll')).toBe('"5"" roll"')
  })

  it('turns rupee amounts into numbers Excel can add up', () => {
    expect(exportValue('₹2,82,056.00')).toBe('282056.00')
    expect(exportValue('-₹1,250.50')).toBe('-1250.50')
    expect(exportValue('1,800 Metre')).toBe('1,800 Metre')
    expect(exportValue('₹645.00 / Set')).toBe('₹645.00 / Set')
  })

  it('starts with a byte-order mark so Excel reads rupees', () => {
    expect(toCsv([['Invoice', 'Total'], ['INV/26-27/00001', '₹1,000.00']])).toBe('\uFEFFInvoice,Total\r\nINV/26-27/00001,"₹1,000.00"')
  })
})

describe('saved views', () => {
  it('keep filters, search and sort but not the page', () => {
    expect(viewSearch('?view=overdue&page=3&q=Tiruppur')).toBe('?q=Tiruppur&view=overdue')
    expect(viewSearch('?page=2')).toBe('')
    expect(viewSearch('')).toBe('')
  })
})
