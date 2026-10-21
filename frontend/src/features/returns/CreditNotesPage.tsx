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
import { useCreditNotes, useCreditNoteSummary, type CreditNoteKind, type CreditNoteListItem } from './api'
import { NoGstBadge, ReturnStatusBadge } from './badges'
import { creditNoteKindLabels, returnReasonLabels } from './labels'

const ALL = 'all'
const CREDIT = 'credit'
const tabs = [
  { value: ALL, label: msg('All') },
  { value: CREDIT, label: msg('Credit left') },
  { value: 'GoodsReturn', label: msg('Goods returned') },
  { value: 'PriceCorrection', label: msg('Price corrections') },
  { value: 'Cancelled', label: msg('Cancelled') },
]

const columns: Column<CreditNoteListItem>[] = [
  {
    key: 'number',
    header: msg('Credit note'),
    cell: (c) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{c.number}</span>
        <span className="text-xs text-muted-foreground">{formatBusinessDate(c.creditNoteDate)}</span>
      </div>
    ),
    className: 'w-40',
  },
  { key: 'customer', header: msg('Customer'), cell: (c) => <span className="font-medium">{c.customerName}</span> },
  { key: 'invoice', header: msg('Against'), cell: (c) => <span className="font-mono text-xs">{c.invoiceNumber}</span> },
  {
    key: 'what',
    header: msg('What'),
    cell: (c) => (
      <div className="flex flex-col">
        <span>{t(creditNoteKindLabels[c.kind])}</span>
        <span className="text-xs text-muted-foreground">{t(returnReasonLabels[c.reason])}</span>
      </div>
    ),
  },
  { key: 'total', header: msg('Total'), align: 'right', cell: (c) => formatINR(c.grandTotal) },
  { key: 'credit', header: msg('Credit left'), align: 'right', cell: (c) => (c.creditAvailable > 0 ? formatINR(c.creditAvailable) : '—') },
  {
    key: 'status',
    header: msg('Status'),
    cell: (c) => (
      <span className="inline-flex flex-wrap gap-1">
        <ReturnStatusBadge status={c.status} />
        {!c.withGst && <NoGstBadge />}
      </span>
    ),
  },
]

function SummaryCards() {
  const summary = useCreditNoteSummary()
  const s = summary.data
  const cards = [
    {
      label: t('Credited this month'),
      value: s && formatINR(s.creditedThisMonth, 0),
      hint: s && tn(s.countThisMonth, '{count} credit note', '{count} credit notes'),
    },
    {
      label: t('Customer credit unused'),
      value: s && formatINR(s.customerCreditAvailable, 0),
      hint: s && tn(s.customersWithCredit, '{count} customer', '{count} customers'),
    },
    { label: t('Refunded this month'), value: s && formatINR(s.refundedThisMonth, 0) },
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

export function CreditNotesPage() {
  const navigate = useNavigate()
  const [params, update] = useListParams()
  const tab = params.filters.view ?? ALL
  const customerId = params.filters.customerId ? Number(params.filters.customerId) : undefined

  const notes = useCreditNotes({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    status: tab === 'Cancelled' ? 'Cancelled' : undefined,
    kind: tab === 'GoodsReturn' || tab === 'PriceCorrection' ? (tab as CreditNoteKind) : undefined,
    withCreditOnly: tab === CREDIT,
    customerId,
  })

  return (
    <>
      <PageHeader
        title={t('Credit notes')}
        description={t(
          "Goods customers sent back and price corrections. Issue one from the invoice; its value comes off what's due, and anything left is the customer's credit.",
        )}
      />
      <SummaryCards />

      <div className="flex flex-wrap items-center gap-3">
        <SegmentedControl label={msg('Show')} value={tab} onValueChange={(v) => update({ filters: { view: v === ALL ? undefined : v } })} options={tabs} />
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder={t('Search credit note, invoice, customer or GSTIN')} />
        {customerId !== undefined && (
          <button type="button" className="text-sm text-muted-foreground underline underline-offset-4" onClick={() => update({ filters: { customerId: undefined } })}>
            {t('Showing one customer · show all')}
          </button>
        )}
      </div>

      <DataTable
        settingsKey="credit-notes"
        columns={columns}
        rows={notes.data?.items}
        getRowKey={(c) => c.id}
        isLoading={notes.isPending}
        error={notes.error}
        onRowClick={(c) => navigate(`/credit-notes/${c.id}`)}
        emptyTitle={tab === CREDIT ? t('No unused credit') : t('No credit notes')}
        emptyDescription={tab === ALL && !params.q ? t('Open an invoice and choose Credit note when goods come back or a price needs correcting.') : undefined}
      />
      {notes.data && (
        <Pagination
          page={notes.data.page}
          pageSize={notes.data.pageSize}
          totalCount={notes.data.totalCount}
          totalPages={notes.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
