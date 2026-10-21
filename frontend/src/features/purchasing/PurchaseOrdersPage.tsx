import { PackageSearch, Plus, X } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { SegmentedControl } from '@/components/SegmentedControl'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { formatBusinessDate, formatINR, formatPercent } from '@/lib/format'
import { usePurchaseOrders, type PurchaseOrderListItem, type PurchaseOrderStatus } from './api'
import { PurchaseOrderStatusBadge } from './badges'
import { ReorderDialog } from './ReorderDialog'
import { msg, t, tc } from '@/lib/i18n'

const ALL = 'all'
const OPEN = 'open'
const LATE = 'late'
// A function, so "Open" can be told apart from the button "Open" (tc) in the language on screen.
const tabs = (): { value: string; label: string }[] => [
  { value: ALL, label: t('All') },
  { value: 'Draft', label: t('Drafts') },
  { value: 'PendingApproval', label: t('To approve') },
  { value: OPEN, label: tc('filter', 'Open') },
  { value: LATE, label: t('Late') },
  { value: 'Received', label: t('Received') },
  { value: 'Closed', label: t('Closed') },
  { value: 'Cancelled', label: t('Cancelled') },
]

const columns: Column<PurchaseOrderListItem>[] = [
  { key: 'number', header: msg('Order'), cell: (o) => <span className="font-mono text-xs">{o.number}</span>, className: 'w-36' },
  { key: 'date', header: msg('Date'), cell: (o) => formatBusinessDate(o.orderDate), className: 'w-28' },
  { key: 'supplier', header: msg('Supplier'), cell: (o) => <span className="font-medium">{o.supplierName}</span> },
  { key: 'lines', header: msg('Items'), align: 'right', cell: (o) => o.lineCount },
  { key: 'total', header: msg('Total'), align: 'right', cell: (o) => formatINR(o.grandTotal, 0) },
  {
    key: 'received',
    header: msg('Received'),
    align: 'right',
    cell: (o) => (o.status === 'Draft' || o.status === 'PendingApproval' || o.status === 'Cancelled' ? '—' : formatPercent(o.receivedPercent * 100)),
  },
  { key: 'expected', header: msg('Expected'), cell: (o) => (o.expectedDate ? formatBusinessDate(o.expectedDate) : '—'), className: 'w-28' },
  { key: 'status', header: msg('Status'), cell: (o) => <PurchaseOrderStatusBadge status={o.status} late={o.isLate} /> },
]

export function PurchaseOrdersPage() {
  const navigate = useNavigate()
  const { can } = useAuth()
  const [params, update] = useListParams()
  const [reorderOpen, setReorderOpen] = useState(false)
  const tab = params.filters.view ?? ALL
  const status = tab !== ALL && tab !== OPEN && tab !== LATE ? (tab as PurchaseOrderStatus) : undefined
  const supplierId = params.filters.supplierId ? Number(params.filters.supplierId) : undefined

  const orders = usePurchaseOrders({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    status,
    supplierId,
    openOnly: tab === OPEN,
    lateOnly: tab === LATE,
  })

  return (
    <>
      <PageHeader
        title={t('Purchase orders')}
        description={t('Draft → ordered (after approval when above the limit) → received. Goods come into stock only against an order.')}
        actions={
          can(Permissions.PurchaseOrdersEdit) && (
            <>
              <Button variant="outline" onClick={() => setReorderOpen(true)}>
                <PackageSearch />
                {t('Reorder low stock')}
              </Button>
              <Button asChild>
                <Link to="/purchase-orders/new">
                  <Plus />
                  {t('New order')}
                </Link>
              </Button>
            </>
          )
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <SegmentedControl label={t('Show')} value={tab} onValueChange={(v) => update({ filters: { view: v === ALL ? undefined : v } })} options={tabs()} />
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder={t('Search order no., supplier or their ref')} />
        {supplierId && (
          <Badge variant="secondary" className="gap-1">
            {t('Supplier: {name}', { name: params.filters.supplier ?? supplierId })}
            <button type="button" aria-label={t('Show all suppliers')} onClick={() => update({ filters: { supplierId: undefined, supplier: undefined } })}>
              <X className="size-3" />
            </button>
          </Badge>
        )}
      </div>

      <DataTable
        settingsKey="purchase-orders"
        columns={columns}
        rows={orders.data?.items}
        getRowKey={(o) => o.id}
        isLoading={orders.isPending}
        error={orders.error}
        onRowClick={(o) => navigate(`/purchase-orders/${o.id}`)}
        emptyTitle={tab === LATE ? t('Nothing is late') : params.q || tab !== ALL ? t('No orders match') : t('No purchase orders yet')}
        emptyDescription={tab === ALL && !params.q ? t('Create an order, or draft them from low stock with "Reorder low stock".') : undefined}
      />
      {orders.data && (
        <Pagination
          page={orders.data.page}
          pageSize={orders.data.pageSize}
          totalCount={orders.data.totalCount}
          totalPages={orders.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}

      {can(Permissions.PurchaseOrdersEdit) && <ReorderDialog open={reorderOpen} onOpenChange={setReorderOpen} />}
    </>
  )
}
