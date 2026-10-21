import { z } from 'zod'
import { integerString, moneyString, optionalText, quantityString, requiredText, toNullIfBlank, toNumberOrNull } from '@/components/form/schemas'
import type { PickedSupplier } from '@/components/SupplierPicker'
import { msg, t } from '@/lib/i18n'
import type { Product, ProductSaveRequest, ProductUnit } from './api'

const units = ['Kg', 'Metre', 'Piece', 'Cone', 'Bale', 'Box', 'Roll', 'Set'] as const satisfies readonly ProductUnit[]

export const productFormSchema = z.object({
  sku: requiredText('SKU', 40).regex(/^[A-Za-z0-9][A-Za-z0-9._/-]*$/, {
    error: msg("SKU may contain letters, digits, '.', '_', '/' and '-', starting with a letter or digit."),
  }),
  name: requiredText(msg('Name'), 200),
  description: optionalText(1000),
  categoryId: z.string().min(1, { error: msg('Choose a category.') }),
  hsnCodeId: z.string().min(1, { error: msg('Choose an HSN code.') }),
  unit: z.enum(units, { error: msg('Choose a unit.') }),
  listPrice: moneyString({ max: 99_999_999 }),
  costPrice: moneyString({ optional: true, max: 99_999_999 }),
  reorderLevel: quantityString,
  reorderQuantity: quantityString,
  leadTimeDays: integerString(0, 365, msg('Lead time')),
  preferredSupplier: z.custom<PickedSupplier | null>(),
  isActive: z.boolean(),
})

export type ProductFormValues = z.infer<typeof productFormSchema>

export const emptyProductForm: ProductFormValues = {
  sku: '',
  name: '',
  description: '',
  categoryId: '',
  hsnCodeId: '',
  unit: 'Kg',
  listPrice: '',
  costPrice: '',
  reorderLevel: '0',
  reorderQuantity: '0',
  leadTimeDays: '7',
  preferredSupplier: null,
  isActive: true,
}

export function toFormValues(p: Product): ProductFormValues {
  return {
    sku: p.sku,
    name: p.name,
    description: p.description ?? '',
    categoryId: p.categoryId.toString(),
    hsnCodeId: p.hsnCodeId.toString(),
    unit: p.unit,
    listPrice: p.listPrice.toString(),
    costPrice: p.costPrice?.toString() ?? '',
    reorderLevel: p.reorderLevel.toString(),
    reorderQuantity: p.reorderQuantity.toString(),
    leadTimeDays: p.leadTimeDays.toString(),
    preferredSupplier:
      p.preferredSupplierId != null
        ? { id: p.preferredSupplierId, code: '', name: p.preferredSupplierName ?? t('Supplier'), gstin: null, city: '', stateName: '' }
        : null,
    isActive: p.isActive,
  }
}

export function toSaveRequest(v: ProductFormValues, rowVersion: string | undefined): ProductSaveRequest {
  return {
    sku: v.sku.trim(),
    name: v.name.trim(),
    description: toNullIfBlank(v.description),
    categoryId: Number(v.categoryId),
    hsnCodeId: Number(v.hsnCodeId),
    unit: v.unit,
    listPrice: Number(v.listPrice),
    costPrice: toNumberOrNull(v.costPrice),
    reorderLevel: Number(v.reorderLevel),
    reorderQuantity: Number(v.reorderQuantity),
    leadTimeDays: Number(v.leadTimeDays),
    preferredSupplierId: v.preferredSupplier?.id ?? null,
    isActive: v.isActive,
    rowVersion: rowVersion ?? null,
  }
}

export const productFieldPaths = [
  'sku', 'name', 'description', 'categoryId', 'hsnCodeId', 'unit', 'listPrice', 'costPrice',
  'reorderLevel', 'reorderQuantity', 'leadTimeDays',
] as const
