import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type AuditEntry = Schemas['AuditEntryDto']
export type AuditAction = Schemas['AuditAction']

export interface AuditQuery {
  q?: string
  page: number
  pageSize: number
  entityType?: string
  entityId?: string
  userId?: string
  action?: AuditAction
  from?: string
  to?: string
}

export function useAudit(query: AuditQuery, enabled = true) {
  return useQuery({
    queryKey: ['audit', query],
    queryFn: () => unwrap(api.GET('/api/audit', { params: { query } })),
    placeholderData: keepPreviousData,
    enabled,
  })
}
