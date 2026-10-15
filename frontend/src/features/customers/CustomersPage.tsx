import { Plus } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { ActiveBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { useStates } from '@/lib/lookups'
import { useCustomers, type CustomerListItem } from './api'

const ALL_STATES = 'all'

const columns: Column<CustomerListItem>[] = [
  { key: 'code', header: 'Code', sortKey: 'code', cell: (c) => <span className="font-mono text-xs">{c.code}</span>, className: 'w-24' },
  { key: 'name', header: 'Name', sortKey: 'name', cell: (c) => <span className="font-medium">{c.name}</span> },
  { key: 'gstin', header: 'GSTIN', cell: (c) => (c.gstin ? <span className="font-mono text-xs">{c.gstin}</span> : <span className="text-muted-foreground">Unregistered</span>) },
  { key: 'city', header: 'City', sortKey: 'city', cell: (c) => `${c.city}, ${c.stateName}` },
  { key: 'phone', header: 'Phone', cell: (c) => c.phone ?? '—' },
  { key: 'rep', header: 'Sales rep', cell: (c) => c.assignedRepName ?? <span className="text-muted-foreground">Unassigned</span> },
  { key: 'status', header: 'Status', cell: (c) => <ActiveBadge active={c.isActive} /> },
]

export function CustomersPage() {
  const navigate = useNavigate()
  const { can } = useAuth()
  const [params, update] = useListParams({ sort: 'name' })
  const states = useStates()
  const stateCode = params.filters.stateCode

  const customers = useCustomers({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    sort: params.sort,
    includeInactive: params.includeInactive,
    stateCode,
  })

  return (
    <>
      <PageHeader
        title="Customers"
        description="Buyers you invoice. GSTIN decides whether invoices carry CGST + SGST or IGST."
        actions={
          can(Permissions.CustomersEdit) && (
            <Button asChild>
              <Link to="/customers/new">
                <Plus />
                New customer
              </Link>
            </Button>
          )
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search name, code, GSTIN, phone, city" />
        <Select
          value={stateCode ?? ALL_STATES}
          onValueChange={(value) => update({ filters: { stateCode: value === ALL_STATES ? undefined : value } })}
        >
          <SelectTrigger className="w-52" aria-label="Filter by state">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_STATES}>All states</SelectItem>
            {states.data?.map((s) => (
              <SelectItem key={s.code} value={s.code}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Switch
            id="show-inactive"
            checked={params.includeInactive}
            onCheckedChange={(includeInactive) => update({ includeInactive })}
          />
          <Label htmlFor="show-inactive">Show inactive</Label>
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={customers.data?.items}
        getRowKey={(c) => c.id}
        isLoading={customers.isPending}
        error={customers.error}
        onRowClick={(c) => navigate(`/customers/${c.id}`)}
        sort={params.sort}
        onSortChange={(sort) => update({ sort })}
        emptyTitle={params.q || stateCode ? 'No customers match' : 'No customers yet'}
        emptyDescription={params.q || stateCode ? 'Try a different search or filter.' : 'Add your first customer to start taking orders.'}
      />

      {customers.data && (
        <Pagination
          page={customers.data.page}
          pageSize={customers.data.pageSize}
          totalCount={customers.data.totalCount}
          totalPages={customers.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
