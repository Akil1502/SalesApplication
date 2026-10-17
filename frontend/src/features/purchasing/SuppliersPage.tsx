import { Plus } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { ActiveBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { formatINR } from '@/lib/format'
import { useSuppliers, type SupplierListItem } from './api'

const columns: Column<SupplierListItem>[] = [
  { key: 'code', header: 'Code', sortKey: 'code', cell: (s) => <span className="font-mono text-xs">{s.code}</span>, className: 'w-24' },
  { key: 'name', header: 'Name', sortKey: 'name', cell: (s) => <span className="font-medium">{s.name}</span> },
  { key: 'gstin', header: 'GSTIN', cell: (s) => (s.gstin ? <span className="font-mono text-xs">{s.gstin}</span> : <span className="text-muted-foreground">Unregistered</span>) },
  { key: 'city', header: 'City', sortKey: 'city', cell: (s) => `${s.city}, ${s.stateName}` },
  { key: 'orders', header: 'Open orders', align: 'right', cell: (s) => s.openOrders || '—' },
  { key: 'payable', header: 'We owe', align: 'right', cell: (s) => (s.payable > 0 ? formatINR(s.payable, 0) : '—') },
  {
    key: 'overdue',
    header: 'Overdue',
    align: 'right',
    cell: (s) => (s.overdue > 0 ? <span className="text-destructive">{formatINR(s.overdue, 0)}</span> : '—'),
  },
  { key: 'status', header: 'Status', cell: (s) => <ActiveBadge active={s.isActive} /> },
]

export function SuppliersPage() {
  const navigate = useNavigate()
  const { can } = useAuth()
  const [params, update] = useListParams({ sort: 'name' })

  const suppliers = useSuppliers({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    sort: params.sort,
    includeInactive: params.includeInactive,
  })

  return (
    <>
      <PageHeader
        title="Suppliers"
        description="Who you buy from. A supplier in another state bills IGST; one in your state bills CGST + SGST."
        actions={
          can(Permissions.SuppliersEdit) && (
            <Button asChild>
              <Link to="/suppliers/new">
                <Plus />
                New supplier
              </Link>
            </Button>
          )
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search name, code, GSTIN, phone, city" />
        <div className="flex items-center gap-2">
          <Switch id="show-inactive" checked={params.includeInactive} onCheckedChange={(includeInactive) => update({ includeInactive })} />
          <Label htmlFor="show-inactive">Show inactive</Label>
        </div>
      </div>

      <DataTable
        settingsKey="suppliers"
        columns={columns}
        rows={suppliers.data?.items}
        getRowKey={(s) => s.id}
        isLoading={suppliers.isPending}
        error={suppliers.error}
        onRowClick={(s) => navigate(`/suppliers/${s.id}`)}
        sort={params.sort}
        onSortChange={(sort) => update({ sort })}
        emptyTitle={params.q ? 'No suppliers match' : 'No suppliers yet'}
        emptyDescription={params.q ? 'Try a different search.' : 'Add the mills and makers you buy from, then place purchase orders with them.'}
      />

      {suppliers.data && (
        <Pagination
          page={suppliers.data.page}
          pageSize={suppliers.data.pageSize}
          totalCount={suppliers.data.totalCount}
          totalPages={suppliers.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
