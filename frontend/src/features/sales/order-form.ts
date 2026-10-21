import { z } from 'zod'
import type { PickedCustomer } from '@/components/CustomerPicker'
import type { PickedProduct } from '@/components/ProductPicker'
import { todayInIndia } from '@/lib/format'
import { msg } from '@/lib/i18n'
import type { Order, OrderSaveRequest } from './api'

const lineSchema = z.object({
  product: z.custom<PickedProduct | null>().refine((p): boolean => p !== null, { error: msg('Choose a product.') }),
  quantity: z
    .string()
    .trim()
    .regex(/^\d+(\.\d{1,3})?$/, { error: msg('Up to 3 decimals.') })
    .refine((v) => Number(v) > 0, { error: msg('More than zero.') }),
  unitPrice: z.string().trim().regex(/^\d+(\.\d{1,2})?$/, { error: msg('Up to 2 decimals.') }),
  discountPercent: z
    .string()
    .trim()
    .regex(/^(\d+(\.\d{1,2})?)?$/, { error: msg('Up to 2 decimals.') })
    .refine((v) => v === '' || Number(v) <= 100, { error: msg('At most 100%.') }),
})

export const orderFormSchema = z
  .object({
    customer: z.custom<PickedCustomer | null>().refine((c): boolean => c !== null, { error: msg('Choose a customer.') }),
    orderDate: z.string().min(1, { error: msg('Choose a date.') }),
    validUntil: z.string(),
    customerReference: z.string().trim().max(50),
    notes: z.string().trim().max(2000),
    lines: z.array(lineSchema).min(1, { error: msg('Add at least one product.') }),
  })
  .superRefine((v, ctx) => {
    if (v.validUntil && v.validUntil < v.orderDate) {
      ctx.addIssue({ code: 'custom', path: ['validUntil'], message: msg("Can't be before the order date.") })
    }
    const seen = new Set<number>()
    v.lines.forEach((line, i) => {
      if (!line.product) return
      if (seen.has(line.product.id)) ctx.addIssue({ code: 'custom', path: ['lines', i, 'product'], message: msg('Already on this order.') })
      seen.add(line.product.id)
    })
  })

export type OrderFormValues = z.infer<typeof orderFormSchema>
export type OrderLineValues = OrderFormValues['lines'][number]

export const emptyLine: OrderLineValues = { product: null, quantity: '', unitPrice: '', discountPercent: '' }

export function emptyOrderForm(): OrderFormValues {
  return { customer: null, orderDate: todayInIndia(), validUntil: '', customerReference: '', notes: '', lines: [emptyLine] }
}

export function toFormValues(order: Order): OrderFormValues {
  return {
    customer: { id: order.customerId, code: order.customerCode, name: order.customerName, gstin: order.customerGstin, city: '', stateName: order.placeOfSupplyStateName },
    orderDate: order.orderDate,
    validUntil: order.validUntil ?? '',
    customerReference: order.customerReference ?? '',
    notes: order.notes ?? '',
    lines: order.lines.map((l) => ({
      product: { id: l.productId, sku: l.sku, name: l.productName, unit: l.unit, quantityOnHand: l.quantityOnHand, price: l.unitPrice, gstRate: l.gstRate },
      quantity: String(l.quantity),
      unitPrice: l.unitPrice.toFixed(2),
      discountPercent: l.discountPercent ? String(l.discountPercent) : '',
    })),
  }
}

export function toSaveRequest(v: OrderFormValues, rowVersion?: string): OrderSaveRequest {
  return {
    customerId: v.customer!.id,
    orderDate: v.orderDate,
    validUntil: v.validUntil || null,
    customerReference: v.customerReference || null,
    notes: v.notes || null,
    source: 'Manual',
    lines: v.lines.map((l) => ({
      productId: l.product!.id,
      quantity: Number(l.quantity),
      unitPrice: Number(l.unitPrice),
      discountPercent: l.discountPercent === '' ? 0 : Number(l.discountPercent),
    })),
    rowVersion: rowVersion ?? null,
  }
}
