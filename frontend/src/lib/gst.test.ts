import { describe, expect, it } from 'vitest'
import { calculateLine, calculateTotals, roundMoney } from './gst'

// Same cases as backend/tests/SalesApp.Tests/Tax/GstCalculatorTests.cs, so the preview and the server agree.
describe('GST calculator (mirrors the server)', () => {
  it('splits intra-state tax into equal CGST and SGST', () => {
    const r = calculateLine({ quantity: 100, unitPrice: 312.5, discountPercent: 0, gstRate: 5 }, false)
    expect([r.taxableValue, r.cgstAmount, r.sgstAmount, r.igstAmount, r.lineTotal]).toEqual([31250, 781.25, 781.25, 0, 32812.5])
  })

  it('charges IGST only on inter-state supplies', () => {
    const r = calculateLine({ quantity: 100, unitPrice: 312.5, discountPercent: 0, gstRate: 5 }, true)
    expect([r.cgstAmount, r.sgstAmount, r.igstAmount]).toEqual([0, 0, 1562.5])
  })

  it('applies the discount before tax, rounding each step to the paisa', () => {
    const r = calculateLine({ quantity: 12.5, unitPrice: 249.99, discountPercent: 7.5, gstRate: 18 }, false)
    expect([r.grossAmount, r.discountAmount, r.taxableValue, r.cgstAmount, r.sgstAmount]).toEqual([3124.88, 234.37, 2890.51, 260.15, 260.15])
  })

  it('rounds half a paisa away from zero', () => {
    expect(calculateLine({ quantity: 1, unitPrice: 0.1, discountPercent: 0, gstRate: 5 }, true).igstAmount).toBe(0.01)
    expect(roundMoney(1.005)).toBe(1.01)
    expect(roundMoney(2.675)).toBe(2.68)
  })

  it('rounds the invoice total to the rupee and reports the round-off', () => {
    const totals = calculateTotals(
      [
        { quantity: 10, unitPrice: 99.95, discountPercent: 0, gstRate: 5 },
        { quantity: 3, unitPrice: 120, discountPercent: 0, gstRate: 18 },
      ],
      false,
    )
    expect([totals.taxableValue, totals.cgstAmount, totals.sgstAmount, totals.roundOff, totals.grandTotal]).toEqual([1359.5, 57.39, 57.39, -0.28, 1474])
  })

  it('can round up', () => {
    const totals = calculateTotals([{ quantity: 1, unitPrice: 99.5, discountPercent: 0, gstRate: 0 }], true)
    expect([totals.roundOff, totals.grandTotal]).toEqual([0.5, 100])
  })
})
