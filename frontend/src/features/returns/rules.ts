/**
 * The last day a credit note or return can change the GST on a document dated `date` (yyyy-mm-dd): 30 November after
 * the end of its financial year (CGST Act s.34(2)). The server applies the same rule; this is for showing it early.
 */
export function gstDeadline(date: string): string {
  const [year, month] = date.split('-').map(Number) as [number, number]
  const financialYearStart = month >= 4 ? year : year - 1
  return `${financialYearStart + 1}-11-30`
}
