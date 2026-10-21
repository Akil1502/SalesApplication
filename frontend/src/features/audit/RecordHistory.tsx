import { ChevronDown, ChevronRight, History } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { formatDateTime } from '@/lib/format'
import { t } from '@/lib/i18n'
import { useAudit } from './api'
import { ActionBadge } from './ActionBadge'
import { ChangeList } from './ChangeList'

/**
 * One record's history: every change with who made it and the old and new values. Closed until opened, and only for
 * users who may see the audit log.
 */
export function RecordHistory({ entityType, entityId }: { entityType: string; entityId: number | string }) {
  const { can } = useAuth()
  const [open, setOpen] = useState(false)
  const [pageSize, setPageSize] = useState(10)
  const history = useAudit({ page: 1, pageSize, entityType, entityId: String(entityId) }, open && can(Permissions.AuditView))
  if (!can(Permissions.AuditView)) return null

  const items = history.data?.items ?? []
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-4">
        <div className="flex flex-col gap-1.5">
          <CardTitle className="flex items-center gap-2">
            <History className="size-4" />
            {t('History')}
          </CardTitle>
          <CardDescription>
            {t('Who changed this {record} and when, with the old and new values.', { record: t(entityType).toLowerCase() })}
          </CardDescription>
        </div>
        <Button type="button" variant="ghost" size="sm" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
          {open ? <ChevronDown /> : <ChevronRight />}
          {open ? t('Hide') : t('Show')}
        </Button>
      </CardHeader>
      {open && (
        <CardContent>
          {history.isPending ? (
            <Skeleton className="h-24 w-full" />
          ) : items.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('No changes recorded yet.')}</p>
          ) : (
            <ol className="flex flex-col gap-4">
              {items.map((entry) => (
                <li key={entry.id} className="flex flex-col gap-2 border-l-2 pl-4">
                  <div className="flex flex-wrap items-center gap-2 text-sm">
                    <ActionBadge action={entry.action} />
                    <span className="font-medium">{entry.userName}</span>
                    <span className="text-muted-foreground">{formatDateTime(entry.occurredAtUtc)}</span>
                  </div>
                  <ChangeList entry={entry} />
                </li>
              ))}
            </ol>
          )}
          {history.data && history.data.totalCount > items.length && (
            <Button type="button" variant="outline" size="sm" className="mt-4" onClick={() => setPageSize((n) => n + 20)}>
              {t('Show older changes')}
            </Button>
          )}
        </CardContent>
      )}
    </Card>
  )
}
