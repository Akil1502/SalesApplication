import { formatAmount, formatBusinessDate, formatQuantity } from '@/lib/format'
import type { ReportColumn, ReportColumnType, ReportValue } from './api'

const countFormatter = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 })
const percentFormatter = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 2 })

export const isNumeric = (type: ReportColumnType) => type !== 'Text' && type !== 'Date'

/** Money columns drop the ₹ from every cell and say it once in the header. */
export const columnHeader = (column: ReportColumn) => (column.type === 'Money' ? `${column.header} (₹)` : column.header)

/** Formats one report cell by its column type. Percent values arrive as fractions (0.25 = 25%). */
export function formatCell(value: ReportValue | undefined, type: ReportColumnType): string {
  if (value === null || value === undefined || value === '') return ''
  // A date column's totals cell holds the word "Total", not a date.
  if (typeof value === 'string') return type === 'Date' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? formatBusinessDate(value) : value
  switch (type) {
    case 'Money':
      return formatAmount(value)
    case 'Quantity':
      return formatQuantity(value)
    case 'Percent':
      return percentFormatter.format(value)
    case 'Count':
      return countFormatter.format(value)
    default:
      return String(value)
  }
}
