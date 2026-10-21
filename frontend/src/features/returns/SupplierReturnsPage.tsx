import { useNavigate } from 'react-router'
import { SegmentedControl } from '@/components/SegmentedControl'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Card, CardContent, CardDescription, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { msg, t, tn } from '@/lib/i18n'
import { useSupplierReturns, useSupplierReturnSummary, type SupplierReturnListItem } from './api'
import { NoGstBadge, ReturnStatusBadge } from './badges'
import { returnReasonLabels } from './labels'

const ALL = 'all'
const AWAITING = 'awaiting'
const CREDIT = 'credit'
const tabs = [
  { value: ALL, label: msg('All') },
  { value: AWAITING, label: msg('Awaiting their credit note') },
  { value: CREDIT, label: msg('Credit left') },
  { value: 'Cancelled', label: msg('Cancelled') },
]

const columns: Column<SupplierReturnListItem>[] = [
  {
    key: 'number',
    header: msg('Debit note'),
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{r.number}</span>
        <span className="text-xs text-muted-foreground">{formatBusinessDate(r.returnDate)}</span>
      </div>
    ),
    className: 'w-40',
  },
  { key: 'supplier', header: msg('Supplier'), cell: (r) => <span className="font-medium">{r.supplierName}</span> },
  {
    key: 'bill',
    header: msg('Against bill'),
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{r.billNumber}</span>
        <span className="text-xs text-muted-foreground">{t('Their {number}', { number: r.supplierInvoiceNumber })}</span>
      </div>
    ),
  },
  { key: 'reason', header: msg('Reason'), cell: (r) => t(returnReasonLabels[r.reason]) },
  {
    key: 'theirs',
    header: msg('Their credit note'),
    cell: (r) => (r.supplierCreditNoteNumber ? <span className="font-mono text-xs">{r.supplierCreditNoteNumber}</span> : t('Awaited')),
  },
  { key: 'total', header: msg('Total'), align: 'right', cell: (r) => formatINR(r.grandTotal) },
  { key: 'credit', header: msg('Credit left'), align: 'right', cell: (r) => (r.creditAvailable > 0 ? formatINR(r.creditAvailable) : '—') },
  {
    key: 'status',
    header: msg('Status'),
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
    {
      label: t('Returned this month'),
      value: s && formatINR(s.returnedThisMonth, 0),
      hint: s && tn(s.countThisMonth, '{count} debit note', '{count} debit notes'),
    },
    { label: t('Credit with suppliers'), value: s && formatINR(s.supplierCreditAvailable, 0) },
    { label: t('Awaiting their credit note'), value: s && String(s.awaitingSupplierNote) },
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
        title={t('Supplier returns')}
        description={t(
          "Goods sent back to suppliers (our debit notes). Return goods from the supplier's bill; the value comes off what we owe and the input GST is reversed.",
        )}
      />
      <SummaryCards />

      <div className="flex flex-wrap items-center gap-3">
        <SegmentedControl label={msg('Show')} value={tab} onValueChange={(v) => update({ filters: { view: v === ALL ? undefined : v } })} options={tabs} />
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder={t('Search debit note, bill, supplier or their credit note')} />
      </div>

      <DataTable
        settingsKey="supplier-returns"
        columns={columns}
        rows={returns.data?.items}
        getRowKey={(r) => r.id}
        isLoading={returns.isPending}
        error={returns.error}
        onRowClick={(r) => navigate(`/supplier-returns/${r.id}`)}
        emptyTitle={tab === AWAITING ? t('No credit notes awaited') : t('No supplier returns')}
        emptyDescription={tab === ALL && !params.q ? t('Open a supplier bill and choose Return goods when something has to go back.') : undefined}
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
