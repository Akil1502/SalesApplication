import { getLanguage, type Language } from '@/lib/i18n'

const inrFormatters = new Map<number, Intl.NumberFormat>()

/**
 * Indian rupees with lakh/crore grouping: 1234567.5 -> "₹12,34,567.50".
 * Formatters are cached per decimal count because Intl.NumberFormat is costly to build.
 */
export function formatINR(amount: number | null | undefined, decimals = 2): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return '—'
  let formatter = inrFormatters.get(decimals)
  if (!formatter) {
    formatter = new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    })
    inrFormatters.set(decimals, formatter)
  }
  return formatter.format(amount)
}

const amountFormatter = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** Money without the ₹ sign, for table columns on printed documents: 1234567.5 -> "12,34,567.50". */
export function formatAmount(amount: number): string {
  return amountFormatter.format(amount)
}

const quantityFormatter = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 3 })

/** Quantities keep up to three decimals (kg to the gram), with Indian digit grouping. */
export function formatQuantity(quantity: number, unit?: string): string {
  const value = quantityFormatter.format(quantity)
  return unit ? `${value} ${unit}` : value
}

const percentFormatter = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 })

export function formatPercent(value: number): string {
  return `${percentFormatter.format(value)}%`
}

const LOCALES: Record<Language, string> = { en: 'en-IN', ta: 'ta-IN' }
const dateFormatters = new Map<string, Intl.DateTimeFormat>()

/** Dates name the month in the language on screen ("13 Oct 2026", "13 அக்., 2026"); digits stay 0–9 in both. */
function dateFormatter(language: Language, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${language}|${JSON.stringify(options)}`
  let formatter = dateFormatters.get(key)
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(LOCALES[language], options)
    dateFormatters.set(key, formatter)
  }
  return formatter
}

const DATE: Intl.DateTimeFormatOptions = { day: '2-digit', month: 'short', year: 'numeric' }

/**
 * Formats a calendar date from the API ("2026-10-13", a DateOnly) as "13 Oct 2026". Parsed and formatted
 * in UTC so the day never shifts with the viewer's time zone. Printed documents pass their own language.
 */
export function formatBusinessDate(dateOnly: string | null | undefined, language: Language = getLanguage()): string {
  if (!dateOnly) return '—'
  const date = new Date(`${dateOnly}T00:00:00Z`)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter(language, { ...DATE, timeZone: 'UTC' }).format(date)
}

const indiaDateFormatter = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' })

/** Today's date in India as "YYYY-MM-DD", the server's notion of "today" for receipts and invoices. */
export function todayInIndia(now = new Date()): string {
  return indiaDateFormatter.format(now)
}

/** "13 Oct 2026" in the viewer's local time zone. */
export function formatDate(isoUtc: string | null | undefined, language: Language = getLanguage()): string {
  if (!isoUtc) return '—'
  const date = new Date(isoUtc)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter(language, DATE).format(date)
}

/** "16 Oct 2026, 11:42 am" in the viewer's local time zone. */
export function formatDateTime(isoUtc: string | null | undefined, language: Language = getLanguage()): string {
  if (!isoUtc) return '—'
  const date = new Date(isoUtc)
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter(language, { ...DATE, hour: '2-digit', minute: '2-digit' }).format(date)
}

/** Any other date shape (a month and year on a chart, a weekday) in the language on screen. */
export function formatDateParts(date: Date, options: Intl.DateTimeFormatOptions, language: Language = getLanguage()): string {
  return dateFormatter(language, options).format(date)
}

const compactFormatter = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 })

/**
 * Short Indian notation for headline figures and chart axes: 4,52,30,000 -> "₹4.52 Cr", 12,34,567 -> "₹12.35 L".
 * Below a lakh the full amount is short enough to show as is.
 */
export function formatINRCompact(amount: number | null | undefined): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return '—'
  const sign = amount < 0 ? '-' : ''
  const abs = Math.abs(amount)
  if (abs >= 1e7) return `${sign}₹${compactFormatter.format(abs / 1e7)} Cr`
  if (abs >= 1e5) return `${sign}₹${compactFormatter.format(abs / 1e5)} L`
  return formatINR(amount, 0)
}
