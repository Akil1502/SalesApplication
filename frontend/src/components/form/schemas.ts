import { z } from 'zod'
import { validateGstin } from '@/lib/gstin'

// Reusable Zod pieces. Numeric inputs are kept as strings in form state (that's what <input> holds)
// and converted when building the API request, which keeps "empty" distinct from 0.

const trimmed = () => z.string().trim()

export const requiredText = (label: string, max: number) =>
  trimmed().min(1, { error: `${label} is required.` }).max(max, { error: `${label} must be ${max} characters or fewer.` })

export const optionalText = (max: number) => trimmed().max(max, { error: `Must be ${max} characters or fewer.` })

export const optionalEmail = trimmed().refine((v) => v === '' || z.email().safeParse(v).success, {
  error: 'Enter a valid email address.',
})

export const optionalPhone = trimmed().refine((v) => v === '' || /^(\+91|0)?[0-9]{10}$/.test(v.replace(/[\s-]/g, '')), {
  error: 'Enter a 10-digit phone number, optionally starting with +91.',
})

export const pincode = trimmed().regex(/^[1-9][0-9]{5}$/, { error: "PIN code must be 6 digits and can't start with 0." })

export const stateCode = z.string().length(2, { error: 'Choose a state.' })

export const optionalGstin = trimmed().superRefine((value, ctx) => {
  if (value === '') return
  const problem = validateGstin(value)
  if (problem) ctx.addIssue({ code: 'custom', message: problem })
})

export const requiredGstin = trimmed().superRefine((value, ctx) => {
  const problem = value === '' ? 'GSTIN is required.' : validateGstin(value)
  if (problem) ctx.addIssue({ code: 'custom', message: problem })
})

/** Non-negative amount with up to two decimals; optional when `optional` is true. */
export const moneyString = (options: { optional?: boolean; max?: number } = {}) =>
  trimmed().superRefine((value, ctx) => {
    if (value === '') {
      if (!options.optional) ctx.addIssue({ code: 'custom', message: 'Enter an amount.' })
      return
    }
    if (!/^\d+(\.\d{1,2})?$/.test(value)) {
      ctx.addIssue({ code: 'custom', message: 'Enter an amount like 1250 or 1250.50.' })
    } else if (options.max !== undefined && Number(value) > options.max) {
      ctx.addIssue({ code: 'custom', message: `Must be at most ${options.max.toLocaleString('en-IN')}.` })
    }
  })

/** Non-negative quantity with up to three decimals. */
export const quantityString = trimmed().regex(/^\d+(\.\d{1,3})?$/, { error: 'Enter a quantity like 500 or 12.5.' })

/** Whole number within a range. */
export const integerString = (min: number, max: number, label: string) =>
  trimmed().superRefine((value, ctx) => {
    const n = Number(value)
    if (!/^\d+$/.test(value) || n < min || n > max) {
      ctx.addIssue({ code: 'custom', message: `${label} must be a whole number from ${min} to ${max}.` })
    }
  })

/** '' -> null, otherwise the number. */
export const toNumberOrNull = (value: string): number | null => (value.trim() === '' ? null : Number(value))

/** '' -> null, otherwise the trimmed string. */
export const toNullIfBlank = (value: string): string | null => (value.trim() === '' ? null : value.trim())
