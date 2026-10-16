import { z } from 'zod'
import type { PickedProduct } from '@/components/ProductPicker'
import type { PickedSupplier } from '@/components/SupplierPicker'
import { todayInIndia } from '@/lib/format'
import type { PurchaseOrder, PurchaseOrderSaveRequest } from './api'

const lineSchema = z.object({
  product: z.custom<PickedProduct | null>().refine((p): boolean => p !== null, { error: 'Choose a product.' }),
  quantity: z
    .string()
    .trim()
    .regex(/^\d+(\.\d{1,3})?$/, { error: 'Up to 3 decimals.' })
    .refine((v) => Number(v) > 0, { error: 'More than zero.' }),
  // Blank: the last price paid to this supplier, else the product's cost (filled in when saved).
  unitPrice: z.string().trim().regex(/^(\d+(\.\d{1,2})?)?$/, { error: 'Up to 2 decimals.' }),
})

export const purchaseOrderFormSchema = z
  .object({
    supplier: z.custom<PickedSupplier | null>().refine((s): boolean => s !== null, { error: 'Choose a supplier.' }),
    orderDate: z.string().min(1, { error: 'Choose a date.' }),
    expectedDate: z.string(),
    supplierReference: z.string().trim().max(50),
    notes: z.string().trim().max(2000),
    lines: z.array(lineSchema).min(1, { error: 'Add at least one product.' }),
  })
  .superRefine((v, ctx) => {
    if (v.expectedDate && v.expectedDate < v.orderDate) {
      ctx.addIssue({ code: 'custom', path: ['expectedDate'], message: "Can't be before the order date." })
    }
    const seen = new Set<number>()
    v.lines.forEach((line, i) => {
      if (!line.product) return
      if (seen.has(line.product.id)) ctx.addIssue({ code: 'custom', path: ['lines', i, 'product'], message: 'Already on this order.' })
      seen.add(line.product.id)
    })
  })

export type PurchaseOrderFormValues = z.infer<typeof purchaseOrderFormSchema>
export type PurchaseOrderLineValues = PurchaseOrderFormValues['lines'][number]

export const emptyLine: PurchaseOrderLineValues = { product: null, quantity: '', unitPrice: '' }

export function emptyPurchaseOrderForm(supplier: PickedSupplier | null = null): PurchaseOrderFormValues {
  return { supplier, orderDate: todayInIndia(), expectedDate: '', supplierReference: '', notes: '', lines: [emptyLine] }
}

export function toFormValues(order: PurchaseOrder): PurchaseOrderFormValues {
  return {
    supplier: {
      id: order.supplierId,
      code: order.supplierCode,
      name: order.supplierName,
      gstin: order.supplierGstin,
      city: order.supplierAddress.city,
      stateName: order.supplierAddress.stateName,
    },
    orderDate: order.orderDate,
    expectedDate: order.expectedDate ?? '',
    supplierReference: order.supplierReference ?? '',
    notes: order.notes ?? '',
    lines: order.lines.map((l) => ({
      product: { id: l.productId, sku: l.sku, name: l.productName, unit: l.unit, quantityOnHand: l.quantityOnHand, price: l.unitPrice, gstRate: l.gstRate },
      quantity: String(l.quantity),
      unitPrice: l.unitPrice.toFixed(2),
    })),
  }
}

export function toSaveRequest(v: PurchaseOrderFormValues, rowVersion?: string): PurchaseOrderSaveRequest {
  return {
    supplierId: v.supplier!.id,
    orderDate: v.orderDate,
    expectedDate: v.expectedDate || null,
    supplierReference: v.supplierReference || null,
    notes: v.notes || null,
    lines: v.lines.map((l) => ({
      productId: l.product!.id,
      quantity: Number(l.quantity),
      unitPrice: l.unitPrice === '' ? null : Number(l.unitPrice),
    })),
    rowVersion: rowVersion ?? null,
  }
}

/** Turns a server key like "lines[1].productId" into the form path "lines.1.product". */
export function toFormPath(key: string): string {
  return key.replace(/\[(\d+)\]/g, '.$1').replace(/\.productId$/, '.product').replace(/^supplierId$/, 'supplier')
}
