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
import { useInvoices, useInvoiceSummary, type InvoiceListItem, type InvoiceStatus } from './api'
import { InvoiceStatusBadge } from './badges'

const ALL = 'all'
const OVERDUE = 'overdue'
const tabs = [
  { value: ALL, label: 'All' },
  { value: OVERDUE, label: 'Overdue' },
  { value: 'Unpaid', label: 'Unpaid' },
  { value: 'PartiallyPaid', label: 'Part paid' },
  { value: 'Paid', label: 'Paid' },
  { value: 'Cancelled', label: 'Cancelled' },
]

const columns: Column<InvoiceListItem>[] = [
  { key: 'number', header: 'Invoice', cell: (i) => <span className="font-mono text-xs">{i.number}</span>, className: 'w-36' },
  { key: 'date', header: 'Date', cell: (i) => formatBusinessDate(i.invoiceDate), className: 'w-28' },
  { key: 'customer', header: 'Customer', cell: (i) => <span className="font-medium">{i.customerName}</span> },
  { key: 'total', header: 'Total', align: 'right', cell: (i) => formatINR(i.grandTotal) },
  { key: 'balance', header: 'Balance due', align: 'right', cell: (i) => (i.balance > 0 ? formatINR(i.balance) : '—') },
  { key: 'due', header: 'Due', cell: (i) => formatBusinessDate(i.dueDate), className: 'w-28' },
  { key: 'status', header: 'Status', cell: (i) => <InvoiceStatusBadge status={i.status} daysOverdue={i.daysOverdue} settledByCredit={i.amountPaid === 0 && i.amountCredited > 0} /> },
]

function SummaryCards() {
  const summary = useInvoiceSummary()
  const s = summary.data
  const cards = [
    { label: 'Outstanding', value: s && formatINR(s.outstandingAmount, 0), hint: s && `${s.outstandingCount} open invoice${s.outstandingCount === 1 ? '' : 's'}` },
    { label: 'Overdue', value: s && formatINR(s.overdueAmount, 0), hint: s && `${s.overdueCount} past due date` },
    { label: 'Invoiced this month', value: s && formatINR(s.invoicedThisMonth, 0) },
    { label: 'Collected this month', value: s && formatINR(s.collectedThisMonth, 0) },
  ]
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
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

export function InvoicesPage() {
  const navigate = useNavigate()
  const [params, update] = useListParams()
  const tab = params.filters.view ?? ALL
  const status = tab !== ALL && tab !== OVERDUE ? (tab as InvoiceStatus) : undefined

  const invoices = useInvoices({ q: params.q || undefined, page: params.page, pageSize: params.pageSize, status, overdueOnly: tab === OVERDUE })

  return (
    <>
      <PageHeader title="Invoices" description="Issued from confirmed orders. Open one to record payments or print it." />
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
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search invoice no., customer or GSTIN" />
      </div>

      <DataTable
        columns={columns}
        rows={invoices.data?.items}
        getRowKey={(i) => i.id}
        isLoading={invoices.isPending}
        error={invoices.error}
        onRowClick={(i) => navigate(`/invoices/${i.id}`)}
        emptyTitle={tab === OVERDUE ? 'Nothing overdue' : 'No invoices'}
        emptyDescription={tab === ALL && !params.q ? 'Invoices are issued from confirmed orders.' : undefined}
      />
      {invoices.data && (
        <Pagination
          page={invoices.data.page}
          pageSize={invoices.data.pageSize}
          totalCount={invoices.data.totalCount}
          totalPages={invoices.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
