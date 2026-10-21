import { z } from 'zod'
import {
  integerString,
  optionalEmail,
  optionalGstin,
  optionalPhone,
  optionalText,
  pincode,
  requiredText,
  stateCode,
  toNullIfBlank,
} from '@/components/form/schemas'
import { gstinStateCode, validateGstin } from '@/lib/gstin'
import { msg, t } from '@/lib/i18n'
import type { Supplier, SupplierSaveRequest } from './api'

export const supplierFormSchema = z
  .object({
    code: optionalText(20).regex(/^[A-Za-z0-9-]*$/, { error: msg("Code may contain letters, digits and '-'.") }),
    name: requiredText(msg('Name'), 150),
    tradeName: optionalText(150),
    gstin: optionalGstin,
    contactPerson: optionalText(100),
    phone: optionalPhone,
    email: optionalEmail,
    address: z.object({
      line1: requiredText(msg('Address line 1'), 120),
      line2: optionalText(120),
      city: requiredText(msg('City'), 60),
      stateCode,
      pincode,
    }),
    paymentTermsDays: integerString(0, 365, msg('Payment terms')),
    bankName: optionalText(100),
    bankAccountNumber: z.string().trim().regex(/^([0-9]{9,18})?$/, { error: msg('Account number must be 9 to 18 digits.') }),
    bankIfsc: z.string().trim().toUpperCase().regex(/^([A-Z]{4}0[A-Z0-9]{6})?$/, { error: msg('IFSC must be 11 characters, like SBIN0001234.') }),
    notes: optionalText(2000),
    isActive: z.boolean(),
    /** Empty: the company's default. */
    documentLanguage: z.string(),
  })
  .superRefine((values, ctx) => {
    // Same rule as the server: a GSTIN is tied to the state it's registered in.
    if (values.gstin && !validateGstin(values.gstin) && values.address.stateCode && gstinStateCode(values.gstin) !== values.address.stateCode) {
      ctx.addIssue({
        code: 'custom',
        path: ['gstin'],
        message: t('This GSTIN is registered in state code {code}, but the address is in a different state.', { code: gstinStateCode(values.gstin) }),
      })
    }
  })

export type SupplierFormValues = z.infer<typeof supplierFormSchema>

export const emptySupplierForm: SupplierFormValues = {
  code: '',
  name: '',
  tradeName: '',
  gstin: '',
  contactPerson: '',
  phone: '',
  email: '',
  address: { line1: '', line2: '', city: '', stateCode: '', pincode: '' },
  paymentTermsDays: '30',
  bankName: '',
  bankAccountNumber: '',
  bankIfsc: '',
  notes: '',
  isActive: true,
  documentLanguage: '',
}

export function toFormValues(s: Supplier): SupplierFormValues {
  return {
    code: s.code,
    name: s.name,
    tradeName: s.tradeName ?? '',
    gstin: s.gstin ?? '',
    contactPerson: s.contactPerson ?? '',
    phone: s.phone ?? '',
    email: s.email ?? '',
    address: { line1: s.address.line1, line2: s.address.line2 ?? '', city: s.address.city, stateCode: s.address.stateCode, pincode: s.address.pincode },
    paymentTermsDays: s.paymentTermsDays.toString(),
    bankName: s.bankName ?? '',
    bankAccountNumber: s.bankAccountNumber ?? '',
    bankIfsc: s.bankIfsc ?? '',
    notes: s.notes ?? '',
    isActive: s.isActive,
    documentLanguage: s.documentLanguage ?? '',
  }
}

export function toSaveRequest(v: SupplierFormValues, rowVersion: string | undefined): SupplierSaveRequest {
  return {
    code: toNullIfBlank(v.code),
    name: v.name.trim(),
    tradeName: toNullIfBlank(v.tradeName),
    gstin: toNullIfBlank(v.gstin),
    contactPerson: toNullIfBlank(v.contactPerson),
    phone: toNullIfBlank(v.phone),
    email: toNullIfBlank(v.email),
    address: {
      line1: v.address.line1.trim(),
      line2: toNullIfBlank(v.address.line2),
      city: v.address.city.trim(),
      stateCode: v.address.stateCode,
      pincode: v.address.pincode.trim(),
    },
    paymentTermsDays: Number(v.paymentTermsDays),
    bankName: toNullIfBlank(v.bankName),
    bankAccountNumber: toNullIfBlank(v.bankAccountNumber),
    bankIfsc: toNullIfBlank(v.bankIfsc),
    notes: toNullIfBlank(v.notes),
    isActive: v.isActive,
    documentLanguage: (toNullIfBlank(v.documentLanguage) as SupplierSaveRequest['documentLanguage']) ?? null,
    rowVersion: rowVersion ?? null,
  }
}

/** Every field path the server may report an error on, for mapping 400 responses onto the form. */
export const supplierFieldPaths = [
  'code', 'name', 'tradeName', 'gstin', 'contactPerson', 'phone', 'email',
  'address.line1', 'address.line2', 'address.city', 'address.stateCode', 'address.pincode',
  'paymentTermsDays', 'bankName', 'bankAccountNumber', 'bankIfsc', 'notes',
] as const
