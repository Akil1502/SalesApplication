import { ExternalLink } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { ActionBadge } from './ActionBadge'
import { useAudit, type AuditAction, type AuditEntry } from './api'
import { actionLabels, changeSummary, isWarning, recordLink, recordTypes } from './audit-format'
import { ChangeList } from './ChangeList'

const ALL = 'all'

/** One entry in full: every changed field, and where it came from. */
function EntryDialog({ entry, onClose }: { entry: AuditEntry | null; onClose: () => void }) {
  const link = entry ? recordLink(entry) : null
  return (
    <Dialog open={entry !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-2xl">
        {entry && (
          <>
            <DialogHeader>
              <DialogTitle className="flex flex-wrap items-center gap-2">
                <ActionBadge action={entry.action} />
                {entry.entityType ? `${entry.entityType}: ${entry.entityLabel ?? entry.entityId}` : actionLabels[entry.action]}
              </DialogTitle>
              <DialogDescription>
                {formatDateTime(entry.occurredAtUtc)} by {entry.userName}
                {entry.ipAddress && `, from ${entry.ipAddress}`}
              </DialogDescription>
            </DialogHeader>
            <div className="flex flex-col gap-4 py-2">
              {entry.summary && entry.changes.length > 0 && <p className="text-sm">{entry.summary}</p>}
              <ChangeList entry={entry} />
              {link && (
                <Button asChild variant="outline" size="sm" className="self-start">
                  <Link to={link}>
                    <ExternalLink />
                    Open {entry.entityType?.toLowerCase()}
                  </Link>
                </Button>
              )}
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

/** Everything that happened: changes with old and new values, sign-ins and downloads, newest first. */
export function AuditLogPage() {
  const [params, update] = useListParams({ pageSize: 50 })
  const { entityType, action, from, to } = params.filters
  const [open, setOpen] = useState<AuditEntry | null>(null)
  const audit = useAudit({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    entityType: entityType || undefined,
    action: (action as AuditAction | undefined) || undefined,
    from: from || undefined,
    to: to || undefined,
  })

  const columns: Column<AuditEntry>[] = [
    { key: 'when', header: 'When', className: 'w-44 whitespace-nowrap', cell: (e) => formatDateTime(e.occurredAtUtc) },
    {
      key: 'who',
      header: 'Who',
      className: 'w-48',
      cell: (e) => (
        <div className="flex flex-col">
          <span className="font-medium">{e.userName}</span>
          {e.ipAddress && <span className="font-mono text-xs text-muted-foreground">{e.ipAddress}</span>}
        </div>
      ),
    },
    {
      key: 'what',
      header: 'What',
      cell: (e) => (
        <div className="flex flex-col items-start gap-1">
          <ActionBadge action={e.action} />
          {e.entityType && (
            <span className="text-sm">
              <span className="text-muted-foreground">{e.entityType}:</span> {e.entityLabel ?? e.entityId}
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'changes',
      header: 'Details',
      className: 'whitespace-normal',
      cell: (e) => <span className={cn('text-sm', isWarning(e.action) && 'text-destructive')}>{changeSummary(e)}</span>,
    },
  ]

  return (
    <>
      <PageHeader
        title="Audit log"
        description="Who changed what and when, with the old and new values, plus sign-ins, access changes and downloads. Entries can't be edited or removed."
      />

      <div className="flex flex-wrap items-end gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search names, records or values" />
        <Select value={entityType ?? ALL} onValueChange={(value) => update({ filters: { entityType: value === ALL ? undefined : value } })}>
          <SelectTrigger className="w-48" aria-label="Filter by record type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All records</SelectItem>
            {recordTypes.map((type) => (
              <SelectItem key={type} value={type}>
                {type}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={action ?? ALL} onValueChange={(value) => update({ filters: { action: value === ALL ? undefined : value } })}>
          <SelectTrigger className="w-44" aria-label="Filter by action">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All actions</SelectItem>
            {(Object.keys(actionLabels) as AuditAction[]).map((a) => (
              <SelectItem key={a} value={a}>
                {actionLabels[a]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Label htmlFor="audit-from">From</Label>
          <Input id="audit-from" type="date" value={from ?? ''} onChange={(e) => update({ filters: { from: e.target.value || undefined } })} className="w-40" />
          <Label htmlFor="audit-to">to</Label>
          <Input id="audit-to" type="date" value={to ?? ''} min={from} onChange={(e) => update({ filters: { to: e.target.value || undefined } })} className="w-40" />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={audit.data?.items}
        getRowKey={(e) => e.id}
        isLoading={audit.isPending}
        error={audit.error}
        onRowClick={setOpen}
        emptyTitle="Nothing recorded yet"
        emptyDescription="Changes, sign-ins and downloads appear here as they happen."
      />
      {audit.data && (
        <Pagination
          page={audit.data.page}
          pageSize={audit.data.pageSize}
          totalCount={audit.data.totalCount}
          totalPages={audit.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
      <EntryDialog entry={open} onClose={() => setOpen(null)} />
    </>
  )
}
