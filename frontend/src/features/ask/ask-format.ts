import type { ReportColumnType } from '@/features/reports/api'
import { columnHeader as reportHeader, formatCell, isNumeric as reportIsNumeric } from '@/features/reports/report-format'
import type { AskColumn, AskColumnKind, AskValue } from './api'

/** Answers are formatted exactly like reports: Indian digit grouping, ₹ once in the header, plain dates. */
const asReportType: Record<AskColumnKind, ReportColumnType> = {
  Text: 'Text',
  Money: 'Money',
  Count: 'Count',
  Number: 'Quantity',
  Date: 'Date',
  Boolean: 'Text',
}

export const isNumericColumn = (column: AskColumn) => reportIsNumeric(asReportType[column.kind])

export const askColumnHeader = (column: AskColumn) => reportHeader({ key: column.name, header: column.name, type: asReportType[column.kind] })

export function formatAskValue(value: AskValue | undefined, column: AskColumn): string {
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  return formatCell(value ?? null, asReportType[column.kind])
}

/** A numeric value for charting; null when the cell isn't a number. */
export function toNumber(value: AskValue | undefined): number | null {
  if (typeof value === 'number') return value
  if (typeof value === 'string' && value.trim() !== '' && !Number.isNaN(Number(value))) return Number(value)
  return null
}
