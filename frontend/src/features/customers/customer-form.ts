import { z } from 'zod'
import {
  integerString,
  moneyString,
  optionalEmail,
  optionalGstin,
  optionalPhone,
  optionalText,
  pincode,
  requiredText,
  stateCode,
  toNullIfBlank,
  toNumberOrNull,
} from '@/components/form/schemas'
import { gstinStateCode, validateGstin } from '@/lib/gstin'
import type { Customer, CustomerSaveRequest } from './api'

const addressSchema = z.object({
  line1: requiredText('Address line 1', 120),
  line2: optionalText(120),
  city: requiredText('City', 60),
  stateCode,
  pincode,
})

// Shipping fields are only validated when they're actually used (see superRefine below).
const looseAddress = z.object({ line1: z.string(), line2: z.string(), city: z.string(), stateCode: z.string(), pincode: z.string() })

export const customerFormSchema = z
  .object({
    code: optionalText(20).regex(/^[A-Za-z0-9-]*$/, { error: "Code may contain letters, digits and '-'." }),
    name: requiredText('Name', 150),
    tradeName: optionalText(150),
    gstin: optionalGstin,
    contactPerson: optionalText(100),
    phone: optionalPhone,
    email: optionalEmail,
    billingAddress: addressSchema,
    shippingSameAsBilling: z.boolean(),
    shippingAddress: looseAddress,
    creditLimit: moneyString({ optional: true, max: 999_999_999 }),
    paymentTermsDays: integerString(0, 365, 'Payment terms'),
    assignedRepId: z.string(),
    notes: optionalText(2000),
    isActive: z.boolean(),
  })
  .superRefine((values, ctx) => {
    if (!values.shippingSameAsBilling) {
      const result = addressSchema.safeParse(values.shippingAddress)
      for (const issue of result.error?.issues ?? []) {
        ctx.addIssue({ code: 'custom', message: issue.message, path: ['shippingAddress', ...issue.path] })
      }
    }

    // Same rule as the server: a GSTIN is tied to the state it's registered in.
    if (values.gstin && !validateGstin(values.gstin) && values.billingAddress.stateCode) {
      if (gstinStateCode(values.gstin) !== values.billingAddress.stateCode) {
        ctx.addIssue({
          code: 'custom',
          path: ['gstin'],
          message: `This GSTIN is registered in state code ${gstinStateCode(values.gstin)}, but the billing address is in a different state.`,
        })
      }
    }
  })

export type CustomerFormValues = z.infer<typeof customerFormSchema>

const emptyAddress = { line1: '', line2: '', city: '', stateCode: '', pincode: '' }

export const emptyCustomerForm: CustomerFormValues = {
  code: '',
  name: '',
  tradeName: '',
  gstin: '',
  contactPerson: '',
  phone: '',
  email: '',
  billingAddress: emptyAddress,
  shippingSameAsBilling: true,
  shippingAddress: emptyAddress,
  creditLimit: '',
  paymentTermsDays: '30',
  assignedRepId: '',
  notes: '',
  isActive: true,
}

export function toFormValues(c: Customer): CustomerFormValues {
  const address = (a: Customer['billingAddress']) => ({
    line1: a.line1,
    line2: a.line2 ?? '',
    city: a.city,
    stateCode: a.stateCode,
    pincode: a.pincode,
  })
  return {
    code: c.code,
    name: c.name,
    tradeName: c.tradeName ?? '',
    gstin: c.gstin ?? '',
    contactPerson: c.contactPerson ?? '',
    phone: c.phone ?? '',
    email: c.email ?? '',
    billingAddress: address(c.billingAddress),
    shippingSameAsBilling: !c.shippingAddress,
    shippingAddress: c.shippingAddress ? address(c.shippingAddress) : emptyAddress,
    creditLimit: c.creditLimit?.toString() ?? '',
    paymentTermsDays: c.paymentTermsDays.toString(),
    assignedRepId: c.assignedRepId ?? '',
    notes: c.notes ?? '',
    isActive: c.isActive,
  }
}

export function toSaveRequest(v: CustomerFormValues, rowVersion: string | undefined): CustomerSaveRequest {
  const address = (a: CustomerFormValues['billingAddress']) => ({
    line1: a.line1.trim(),
    line2: toNullIfBlank(a.line2),
    city: a.city.trim(),
    stateCode: a.stateCode,
    pincode: a.pincode.trim(),
  })
  return {
    code: toNullIfBlank(v.code),
    name: v.name.trim(),
    tradeName: toNullIfBlank(v.tradeName),
    gstin: toNullIfBlank(v.gstin),
    contactPerson: toNullIfBlank(v.contactPerson),
    phone: toNullIfBlank(v.phone),
    email: toNullIfBlank(v.email),
    billingAddress: address(v.billingAddress),
    shippingSameAsBilling: v.shippingSameAsBilling,
    shippingAddress: v.shippingSameAsBilling ? null : address(v.shippingAddress),
    creditLimit: toNumberOrNull(v.creditLimit),
    paymentTermsDays: Number(v.paymentTermsDays),
    assignedRepId: toNullIfBlank(v.assignedRepId),
    notes: toNullIfBlank(v.notes),
    isActive: v.isActive,
    rowVersion: rowVersion ?? null,
  }
}

/** Every field path the server may report an error on, for mapping 400 responses onto the form. */
export const customerFieldPaths = [
  'code', 'name', 'tradeName', 'gstin', 'contactPerson', 'phone', 'email',
  'billingAddress.line1', 'billingAddress.line2', 'billingAddress.city', 'billingAddress.stateCode', 'billingAddress.pincode',
  'shippingAddress', 'shippingAddress.line1', 'shippingAddress.line2', 'shippingAddress.city', 'shippingAddress.stateCode', 'shippingAddress.pincode',
  'creditLimit', 'paymentTermsDays', 'assignedRepId', 'notes',
] as const
