import { Plus, Sparkles } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { SegmentedControl } from '@/components/SegmentedControl'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { useAiStatus } from '@/features/ai/api'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { useOrders, type OrderListItem, type OrderStatus } from './api'
import { OrderStatusBadge } from './badges'

const ALL = 'all'
const statusTabs: { value: OrderStatus | typeof ALL; label: string }[] = [
  { value: ALL, label: 'All' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Quoted', label: 'Quoted' },
  { value: 'Confirmed', label: 'To invoice' },
  { value: 'Invoiced', label: 'Invoiced' },
  { value: 'Cancelled', label: 'Cancelled' },
]

const columns: Column<OrderListItem>[] = [
  {
    key: 'number',
    header: 'Order',
    cell: (o) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{o.number}</span>
        {o.quotationNumber && <span className="font-mono text-xs text-muted-foreground">{o.quotationNumber}</span>}
      </div>
    ),
    className: 'w-36',
  },
  { key: 'date', header: 'Date', cell: (o) => formatBusinessDate(o.orderDate), className: 'w-28' },
  {
    key: 'customer',
    header: 'Customer',
    cell: (o) => (
      <span className="inline-flex items-center gap-1.5 font-medium">
        {o.customerName}
        {o.source !== 'Manual' && <Sparkles className="size-3.5 text-violet-600" aria-label="Captured by AI" />}
      </span>
    ),
  },
  { key: 'lines', header: 'Items', align: 'right', cell: (o) => o.lineCount },
  { key: 'total', header: 'Total', align: 'right', cell: (o) => formatINR(o.grandTotal, 0) },
  { key: 'status', header: 'Status', cell: (o) => <OrderStatusBadge status={o.status} /> },
  { key: 'invoice', header: 'Invoice', cell: (o) => (o.invoiceNumber ? <span className="font-mono text-xs">{o.invoiceNumber}</span> : '—') },
  { key: 'by', header: 'Created by', cell: (o) => o.createdByName },
]

export function OrdersPage() {
  const navigate = useNavigate()
  const { can } = useAuth()
  const [params, update] = useListParams()
  const status = params.filters.status as OrderStatus | undefined

  const orders = useOrders({ q: params.q || undefined, page: params.page, pageSize: params.pageSize, status })
  const ai = useAiStatus()

  return (
    <>
      <PageHeader
        title="Orders & quotations"
        description="Draft → quoted → confirmed → invoiced. Confirmed orders are ready to invoice."
        actions={
          <>
            {ai.data?.orderEntry && can(Permissions.SmartEntryUse) && (
              <Button asChild variant="outline">
                <Link to="/orders/smart-entry">
                  <Sparkles />
                  Smart entry
                </Link>
              </Button>
            )}
            {can(Permissions.OrdersEdit) && (
              <Button asChild>
                <Link to="/orders/new">
                  <Plus />
                  New order
                </Link>
              </Button>
            )}
          </>
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <SegmentedControl label="Show" value={status ?? ALL} onValueChange={(v) => update({ filters: { status: v === ALL ? undefined : v } })} options={statusTabs} />
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search order, quotation, customer or PO" />
      </div>

      <DataTable
        settingsKey="orders"
        columns={columns}
        rows={orders.data?.items}
        getRowKey={(o) => o.id}
        isLoading={orders.isPending}
        error={orders.error}
        onRowClick={(o) => navigate(`/orders/${o.id}`)}
        emptyTitle={params.q || status ? 'No orders match' : 'No orders yet'}
        emptyDescription={params.q || status ? undefined : 'Create a quotation or order to get started.'}
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
    </>
  )
}
