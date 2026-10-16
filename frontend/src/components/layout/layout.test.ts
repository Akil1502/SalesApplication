import { describe, expect, it } from 'vitest'
import { isRecordMonth, type TrendPoint } from '@/features/dashboard/trend'
import { allowedNavGroups } from './navigation'
import { panelListOf } from './record-panel-state'

const point = (value: number, partial = false): TrendPoint => ({ tick: '', month: String(value), value, invoices: 1, partial })

describe('record panels', () => {
  it('knows which list a record belongs to', () => {
    expect(panelListOf('/invoices')).toBe('/invoices')
    expect(panelListOf('/invoices/42')).toBe('/invoices')
    expect(panelListOf('/invoices/42/credit-note')).toBe('/invoices')
    expect(panelListOf('/stock/receipts/7')).toBe('/stock')
    expect(panelListOf('/orders/new')).toBe('/orders')
  })

  it('leaves pages of their own alone', () => {
    expect(panelListOf('/orders/smart-entry')).toBeUndefined()
    expect(panelListOf('/reports/gst-summary')).toBeUndefined()
    expect(panelListOf('/invoices-archive')).toBeUndefined()
  })
})

describe('menus', () => {
  it('show only what the user may open, and AI pages only while AI is on', () => {
    const can = (...anyOf: string[]) => anyOf.some((p) => p === 'invoices.view' || p === 'ask-data.use')
    expect(allowedNavGroups(can, false).map((g) => g.items.map((i) => i.title))).toEqual([['Invoices']])
    expect(allowedNavGroups(can, true).flatMap((g) => g.items.map((i) => i.title))).toEqual(['Invoices', 'Ask your data'])
  })
})

describe('celebrations', () => {
  it('only when this month already beats every earlier one', () => {
    expect(isRecordMonth([point(10), point(12), point(11), point(13, true)])).toBe(true)
    expect(isRecordMonth([point(10), point(12), point(11), point(12, true)])).toBe(false)
    // Too little history to call it a record.
    expect(isRecordMonth([point(1), point(5, true)])).toBe(false)
  })
})
