import { t } from '@/lib/i18n'
/**
 * GSTIN validation, mirroring SalesApp.Core.Validation.Gstin on the server so users see mistakes
 * as they type. The server remains the authority.
 */

const CHARSET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'
const FORMAT = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/

export function normalizeGstin(value: string): string {
  return value.replace(/\s+/g, '').toUpperCase()
}

/** GSTN's mod-36 check character over the first 14 characters. */
export function gstinCheckCharacter(first14: string): string {
  let sum = 0
  for (let i = 0; i < 14; i++) {
    const value = CHARSET.indexOf(first14[i] ?? '')
    if (value < 0) throw new Error(`'${first14[i]}' is not a valid GSTIN character.`)
    const product = value * (i % 2 === 0 ? 1 : 2)
    sum += Math.floor(product / 36) + (product % 36)
  }
  return CHARSET[(36 - (sum % 36)) % 36]!
}

/** Returns null when valid, otherwise a message for the field, in the language on screen. */
export function validateGstin(value: string): string | null {
  const gstin = normalizeGstin(value)
  if (gstin.length !== 15) return t('GSTIN must be exactly 15 characters.')
  if (!FORMAT.test(gstin)) {
    return t("GSTIN format is invalid. Expected 2-digit state code, 10-character PAN, entity number, 'Z' and a check character.")
  }
  if (gstinCheckCharacter(gstin.slice(0, 14)) !== gstin[14]) {
    return t("GSTIN check character doesn't match. Please re-check the number for typos.")
  }
  return null
}

export function gstinStateCode(gstin: string): string {
  return normalizeGstin(gstin).slice(0, 2)
}

export function gstinPan(gstin: string): string {
  return normalizeGstin(gstin).slice(2, 12)
}
