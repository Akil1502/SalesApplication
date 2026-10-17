import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { ApiRequestError, isApiError, unwrap } from '@/lib/api/errors'

export type ReportDefinition = Schemas['ReportDefinitionDto']
export type ReportColumn = Schemas['ReportColumn']
export type ReportColumnType = Schemas['ReportColumnType']
export type Dashboard = Schemas['DashboardDto']
export type RankedItem = Schemas['RankedItemDto']

/** A report cell: text, a number (money, quantity, count, or a fraction for percent), a date string, or empty. */
export type ReportValue = string | number | null
export type ReportRow = Record<string, ReportValue>

/** The generated type can't describe the dictionary rows, so restate them. */
export type ReportResult = Omit<Schemas['ReportResult'], 'rows' | 'totals'> & { rows: ReportRow[]; totals: ReportRow }

export interface ReportQuery {
  from?: string
  to?: string
  period?: 'month' | 'day'
}

export const reportKeys = {
  all: ['reports'] as const,
  definitions: ['reports', 'definitions'] as const,
  dashboard: ['dashboard'] as const,
}

/** The dashboard's figures for a period; the previous period's stay on screen while the next one loads. */
export function useDashboard(period: Schemas['DashboardPeriod']) {
  return useQuery({
    queryKey: [...reportKeys.dashboard, period],
    queryFn: () => unwrap(api.GET('/api/dashboard', { params: { query: { period } } })),
    placeholderData: keepPreviousData,
  })
}

export function useReportDefinitions() {
  return useQuery({ queryKey: reportKeys.definitions, queryFn: () => unwrap(api.GET('/api/reports')), staleTime: Infinity })
}

export function useReport(key: string, query: ReportQuery, enabled = true) {
  return useQuery({
    queryKey: ['reports', key, query],
    enabled,
    queryFn: async () => (await unwrap(api.GET('/api/reports/{key}', { params: { path: { key }, query } }))) as unknown as ReportResult,
    placeholderData: keepPreviousData,
  })
}

/** Fetches the Excel version with the signed-in user's token (a plain link can't send it) and saves it. */
export async function downloadReportExcel(key: string, query: ReportQuery): Promise<void> {
  const { data, error, response } = await api.GET('/api/reports/{key}/excel', {
    params: { path: { key }, query },
    parseAs: 'blob',
  })
  if (!response.ok || !data) {
    throw new ApiRequestError(response.status, isApiError(error) ? error : { message: 'The Excel file could not be created.', traceId: '' })
  }

  const fileName = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? `${key}.xlsx`
  const url = URL.createObjectURL(data as Blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  // Revoking straight away can cancel the download in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
