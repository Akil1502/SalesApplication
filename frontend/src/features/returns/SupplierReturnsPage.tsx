import { useNavigate } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Card, CardContent, CardDescription, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { useSupplierReturns, useSupplierReturnSummary, type SupplierReturnListItem } from './api'
import { NoGstBadge, ReturnStatusBadge } from './badges'
import { returnReasonLabels } from './labels'

const ALL = 'all'
const AWAITING = 'awaiting'
const CREDIT = 'credit'
const tabs = [
  { value: ALL, label: 'All' },
  { value: AWAITING, label: 'Awaiting their credit note' },
  { value: CREDIT, label: 'Credit left' },
  { value: 'Cancelled', label: 'Cancelled' },
]

const columns: Column<SupplierReturnListItem>[] = [
  {
    key: 'number',
    header: 'Debit note',
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{r.number}</span>
        <span className="text-xs text-muted-foreground">{formatBusinessDate(r.returnDate)}</span>
      </div>
    ),
    className: 'w-40',
  },
  { key: 'supplier', header: 'Supplier', cell: (r) => <span className="font-medium">{r.supplierName}</span> },
  {
    key: 'bill',
    header: 'Against bill',
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{r.billNumber}</span>
        <span className="text-xs text-muted-foreground">Their {r.supplierInvoiceNumber}</span>
      </div>
    ),
  },
  { key: 'reason', header: 'Reason', cell: (r) => returnReasonLabels[r.reason] },
  { key: 'theirs', header: 'Their credit note', cell: (r) => (r.supplierCreditNoteNumber ? <span className="font-mono text-xs">{r.supplierCreditNoteNumber}</span> : 'Awaited') },
  { key: 'total', header: 'Total', align: 'right', cell: (r) => formatINR(r.grandTotal) },
  { key: 'credit', header: 'Credit left', align: 'right', cell: (r) => (r.creditAvailable > 0 ? formatINR(r.creditAvailable) : '—') },
  {
    key: 'status',
    header: 'Status',
    cell: (r) => (
      <span className="inline-flex flex-wrap gap-1">
        <ReturnStatusBadge status={r.status} />
        {!r.withGst && <NoGstBadge />}
      </span>
    ),
  },
]

function SummaryCards() {
  const summary = useSupplierReturnSummary()
  const s = summary.data
  const cards = [
    { label: 'Returned this month', value: s && formatINR(s.returnedThisMonth, 0), hint: s && `${s.countThisMonth} debit note${s.countThisMonth === 1 ? '' : 's'}` },
    { label: 'Credit with suppliers', value: s && formatINR(s.supplierCreditAvailable, 0) },
    { label: 'Awaiting their credit note', value: s && String(s.awaitingSupplierNote) },
  ]
  return (
    <div className="grid gap-4 sm:grid-cols-3">
      {cards.map((card) => (
        <Card key={card.label} className="gap-2 py-4">
          <CardHeader className="px-4">
            <CardDescription>{card.label}</CardDescription>
          </CardHeader>
          <CardContent className="px-4">
            {summary.isPending ? <Skeleton className="h-7 w-24" /> : <p className="text-2xl font-semibold tabular-nums">{card.value}</p>}
            {card.hint && <p className="text-xs text-muted-foreground">{card.hint}</p>}
          </CardContent>
        </Card>
      ))}
    </div>
  )
}

export function SupplierReturnsPage() {
  const navigate = useNavigate()
  const [params, update] = useListParams()
  const tab = params.filters.view ?? ALL

  const returns = useSupplierReturns({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    status: tab === 'Cancelled' ? 'Cancelled' : undefined,
    awaitingSupplierNoteOnly: tab === AWAITING,
    withCreditOnly: tab === CREDIT,
  })

  return (
    <>
      <PageHeader
        title="Supplier returns"
        description="Goods sent back to suppliers (our debit notes). Return goods from the supplier's bill; the value comes off what we owe and the input GST is reversed."
      />
      <SummaryCards />

      <div className="flex flex-wrap items-center gap-3">
        <Tabs value={tab} onValueChange={(v) => update({ filters: { view: v === ALL ? undefined : v } })}>
          <TabsList>
            {tabs.map((t) => (
              <TabsTrigger key={t.value} value={t.value}>
                {t.label}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search debit note, bill, supplier or their credit note" />
      </div>

      <DataTable
        columns={columns}
        rows={returns.data?.items}
        getRowKey={(r) => r.id}
        isLoading={returns.isPending}
        error={returns.error}
        onRowClick={(r) => navigate(`/supplier-returns/${r.id}`)}
        emptyTitle={tab === AWAITING ? 'No credit notes awaited' : 'No supplier returns'}
        emptyDescription={tab === ALL && !params.q ? 'Open a supplier bill and choose Return goods when something has to go back.' : undefined}
      />
      {returns.data && (
        <Pagination
          page={returns.data.page}
          pageSize={returns.data.pageSize}
          totalCount={returns.data.totalCount}
          totalPages={returns.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
