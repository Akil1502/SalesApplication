import { useQuery } from '@tanstack/react-query'
import { FilePlus2 } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { SegmentedControl } from '@/components/SegmentedControl'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader } from '@/components/ui/card'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { api } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { useSupplierBills, useSupplierBillSummary, type SupplierBillListItem, type SupplierBillStatus } from './api'
import { BillStatusBadge, MatchBadge, PurchaseOrderStatusBadge } from './badges'
import { msg, t, tn } from '@/lib/i18n'

const ALL = 'all'
const APPROVAL = 'approval'
const UNPAID = 'unpaid'
const OVERDUE = 'overdue'
const tabs = [
  { value: ALL, label: msg('All') },
  { value: APPROVAL, label: msg('Needs approval') },
  { value: UNPAID, label: msg('Unpaid') },
  { value: OVERDUE, label: msg('Overdue') },
  { value: 'Paid', label: msg('Paid') },
  { value: 'Cancelled', label: msg('Cancelled') },
]

const columns: Column<SupplierBillListItem>[] = [
  {
    key: 'number',
    header: msg('Bill'),
    cell: (b) => (
      <div className="flex flex-col">
        <span className="font-mono text-xs">{b.number}</span>
        <span className="text-xs text-muted-foreground">{t('Their {number}', { number: b.supplierInvoiceNumber })}</span>
      </div>
    ),
    className: 'w-40',
  },
  { key: 'date', header: msg('Date'), cell: (b) => formatBusinessDate(b.billDate), className: 'w-28' },
  { key: 'supplier', header: msg('Supplier'), cell: (b) => <span className="font-medium">{b.supplierName}</span> },
  { key: 'order', header: msg('Order'), cell: (b) => <span className="font-mono text-xs">{b.purchaseOrderNumber}</span> },
  { key: 'total', header: msg('Total'), align: 'right', cell: (b) => formatINR(b.grandTotal) },
  { key: 'balance', header: msg('To pay'), align: 'right', cell: (b) => (b.balance > 0 ? formatINR(b.balance) : '—') },
  { key: 'due', header: msg('Due'), cell: (b) => formatBusinessDate(b.dueDate), className: 'w-28' },
  {
    key: 'status',
    header: msg('Status'),
    cell: (b) => (
      <span className="inline-flex flex-wrap gap-1">
        {b.matchStatus !== 'Matched' && b.status !== 'Cancelled' && <MatchBadge status={b.matchStatus} />}
        <BillStatusBadge status={b.status} daysOverdue={b.daysOverdue} />
      </span>
    ),
  },
]

function SummaryCards() {
  const summary = useSupplierBillSummary()
  const s = summary.data
  const cards = [
    { label: t('We owe'), value: s && formatINR(s.payable, 0), hint: s && tn(s.openBills, '{count} unpaid bill', '{count} unpaid bills') },
    { label: t('Overdue'), value: s && formatINR(s.overdue, 0), hint: s && t('{count} past due date', { count: s.overdueBills }) },
    { label: t('Due in the next 7 days'), value: s && formatINR(s.dueNextSevenDays, 0) },
    {
      label: t('Paid this month'),
      value: s && formatINR(s.paidThisMonth, 0),
      hint: s && s.needsApproval > 0 ? tn(s.needsApproval, '{count} bill waiting for approval', '{count} bills waiting for approval') : undefined,
    },
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

/** Choose the purchase order a bill is for; the bill form starts from its lines. */
function ChooseOrderDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const orders = useQuery({
    queryKey: ['purchase-orders', 'bill-picker', search.trim()],
    queryFn: () => unwrap(api.GET('/api/purchase-orders', { params: { query: { q: search.trim() || undefined, pageSize: 20 } } })),
    enabled: open,
    staleTime: 30_000,
  })
  const billable = (orders.data?.items ?? []).filter((o) => o.status === 'Ordered' || o.status === 'PartlyReceived' || o.status === 'Received' || o.status === 'Closed')

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="p-0 sm:max-w-xl">
        <DialogHeader className="px-4 pt-4">
          <DialogTitle>{t('Which order is the bill for?')}</DialogTitle>
          <DialogDescription>{t('Bills are entered against the purchase order, so they can be checked against it and against what arrived.')}</DialogDescription>
        </DialogHeader>
        <Command shouldFilter={false}>
          <CommandInput placeholder={t('Search order no. or supplier…')} value={search} onValueChange={setSearch} />
          <CommandList>
            <CommandEmpty>{orders.isFetching ? t('Searching…') : t('No placed orders match.')}</CommandEmpty>
            <CommandGroup>
              {billable.map((o) => (
                <CommandItem key={o.id} value={String(o.id)} onSelect={() => navigate(`/supplier-bills/new?purchaseOrderId=${o.id}`)}>
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate">
                      <span className="font-mono text-xs">{o.number}</span> · {o.supplierName}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {formatBusinessDate(o.orderDate)} · {formatINR(o.grandTotal, 0)}
                    </span>
                  </div>
                  <PurchaseOrderStatusBadge status={o.status} />
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </DialogContent>
    </Dialog>
  )
}

export function SupplierBillsPage() {
  const navigate = useNavigate()
  const { can } = useAuth()
  const [params, update] = useListParams()
  const [choosing, setChoosing] = useState(false)
  const tab = params.filters.view ?? ALL
  const status = tab !== ALL && tab !== APPROVAL && tab !== UNPAID && tab !== OVERDUE ? (tab as SupplierBillStatus) : undefined

  const bills = useSupplierBills({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    status,
    matchStatus: tab === APPROVAL ? 'NeedsApproval' : undefined,
    unpaidOnly: tab === UNPAID,
    overdueOnly: tab === OVERDUE,
  })

  return (
    <>
      <PageHeader
        title={t('Supplier bills')}
        description={t("Each bill is checked against its order's prices and what arrived. A bill that doesn't match waits for approval before it can be paid.")}
        actions={
          can(Permissions.SupplierBillsEdit) && (
            <Button onClick={() => setChoosing(true)}>
              <FilePlus2 />
              {t('Enter bill')}
            </Button>
          )
        }
      />
      <SummaryCards />

      <div className="flex flex-wrap items-center gap-3">
        <SegmentedControl label={t('Show')} value={tab} onValueChange={(v) => update({ filters: { view: v === ALL ? undefined : v } })} options={tabs} />
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder={t('Search bill, their invoice no., supplier or order')} />
      </div>

      <DataTable
        settingsKey="supplier-bills"
        columns={columns}
        rows={bills.data?.items}
        getRowKey={(b) => b.id}
        isLoading={bills.isPending}
        error={bills.error}
        onRowClick={(b) => navigate(`/supplier-bills/${b.id}`)}
        emptyTitle={tab === OVERDUE ? t('Nothing overdue') : tab === APPROVAL ? t('Nothing waiting for approval') : t('No bills')}
        emptyDescription={tab === ALL && !params.q ? t('Enter a supplier bill from its purchase order once the goods have arrived.') : undefined}
      />
      {bills.data && (
        <Pagination
          page={bills.data.page}
          pageSize={bills.data.pageSize}
          totalCount={bills.data.totalCount}
          totalPages={bills.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}

      <ChooseOrderDialog open={choosing} onOpenChange={setChoosing} />
    </>
  )
}
