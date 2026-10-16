import { describe, expect, it } from 'vitest'
import { purchaseOrderFormSchema, toFormPath, toSaveRequest, type PurchaseOrderFormValues } from './purchase-order-form'

const supplier = { id: 3, code: 'S00003', name: 'Lakshmi Spinning Mills', gstin: null, city: 'Coimbatore', stateName: 'Tamil Nadu' }
const yarn = { id: 7, sku: 'CY-30C', name: 'Cotton yarn 30s combed', unit: 'Kg' as const, quantityOnHand: 100, price: 255.65, gstRate: 5 }

function values(overrides: Partial<PurchaseOrderFormValues> = {}): PurchaseOrderFormValues {
  return {
    supplier,
    orderDate: '2026-10-16',
    expectedDate: '',
    supplierReference: '',
    notes: '',
    lines: [{ product: yarn, quantity: '500', unitPrice: '' }],
    ...overrides,
  }
}

describe('purchase order form', () => {
  it('leaves a blank price for the server to fill with the last price paid', () => {
    const request = toSaveRequest(values({ lines: [{ product: yarn, quantity: '500.125', unitPrice: '' }] }), 'rv1')
    expect(request.lines).toEqual([{ productId: 7, quantity: 500.125, unitPrice: null }])
    expect(request.supplierId).toBe(3)
    expect(request.expectedDate).toBeNull()
    expect(request.rowVersion).toBe('rv1')

    const priced = toSaveRequest(values({ lines: [{ product: yarn, quantity: '10', unitPrice: '260.50' }] }))
    expect(priced.lines[0]!.unitPrice).toBe(260.5)
  })

  it('rejects the same product twice and a delivery date before the order date', () => {
    const result = purchaseOrderFormSchema.safeParse(
      values({
        expectedDate: '2026-10-01',
        lines: [
          { product: yarn, quantity: '1', unitPrice: '' },
          { product: yarn, quantity: '2', unitPrice: '' },
        ],
      }),
    )
    expect(result.success).toBe(false)
    const paths = result.error!.issues.map((i) => i.path.join('.'))
    expect(paths).toContain('expectedDate')
    expect(paths).toContain('lines.1.product')
  })

  it('needs a supplier, a positive quantity and at most two decimals in the price', () => {
    const result = purchaseOrderFormSchema.safeParse(values({ supplier: null, lines: [{ product: yarn, quantity: '0', unitPrice: '1.234' }] }))
    const paths = result.error!.issues.map((i) => i.path.join('.'))
    expect(paths).toEqual(expect.arrayContaining(['supplier', 'lines.0.quantity', 'lines.0.unitPrice']))
  })

  it('maps server field keys onto form fields', () => {
    expect(toFormPath('lines[2].productId')).toBe('lines.2.product')
    expect(toFormPath('lines[0].quantity')).toBe('lines.0.quantity')
    expect(toFormPath('supplierId')).toBe('supplier')
  })
})
