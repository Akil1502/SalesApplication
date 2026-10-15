import { Link, useNavigate } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { usePayments, type Payment, type PaymentMode } from './api'
import { paymentModeLabel, paymentModes } from './labels'

const ALL = 'all'

const columns: Column<Payment>[] = [
  { key: 'date', header: 'Date', cell: (p) => formatBusinessDate(p.paymentDate), className: 'w-28' },
  {
    key: 'invoice',
    header: 'Invoice',
    cell: (p) => (
      <Link className="font-mono text-xs underline-offset-4 hover:underline" to={`/invoices/${p.invoiceId}`} onClick={(e) => e.stopPropagation()}>
        {p.invoiceNumber}
      </Link>
    ),
  },
  { key: 'customer', header: 'Customer', cell: (p) => <span className="font-medium">{p.customerName}</span> },
  { key: 'mode', header: 'Mode', cell: (p) => paymentModeLabel(p.mode) },
  { key: 'ref', header: 'Reference', cell: (p) => <span className="font-mono text-xs">{p.reference ?? '—'}</span> },
  { key: 'by', header: 'Recorded by', cell: (p) => p.createdByName },
  { key: 'amount', header: 'Amount', align: 'right', cell: (p) => formatINR(p.amount) },
]

export function PaymentsPage() {
  const navigate = useNavigate()
  const [params, update] = useListParams()
  const mode = params.filters.mode as PaymentMode | undefined
  const { from, to } = params.filters

  const payments = usePayments({ q: params.q || undefined, page: params.page, pageSize: params.pageSize, mode, from, to })

  return (
    <>
      <PageHeader title="Payments" description="Money received against invoices. Record a payment from the invoice it settles." />

      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search invoice, customer or reference" />
        <Select value={mode ?? ALL} onValueChange={(v) => update({ filters: { mode: v === ALL ? undefined : v } })}>
          <SelectTrigger className="w-40" aria-label="Filter by payment mode">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All modes</SelectItem>
            {paymentModes.map((m) => (
              <SelectItem key={m.value} value={m.value}>
                {m.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Label htmlFor="from" className="text-muted-foreground">
            From
          </Label>
          <Input id="from" type="date" className="w-40" value={from ?? ''} onChange={(e) => update({ filters: { from: e.target.value || undefined } })} />
          <Label htmlFor="to" className="text-muted-foreground">
            to
          </Label>
          <Input id="to" type="date" className="w-40" value={to ?? ''} onChange={(e) => update({ filters: { to: e.target.value || undefined } })} />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={payments.data?.items}
        getRowKey={(p) => p.id}
        isLoading={payments.isPending}
        error={payments.error}
        onRowClick={(p) => navigate(`/invoices/${p.invoiceId}`)}
        emptyTitle="No payments"
        emptyDescription="Payments appear here once recorded against invoices."
      />
      {payments.data && (
        <Pagination
          page={payments.data.page}
          pageSize={payments.data.pageSize}
          totalCount={payments.data.totalCount}
          totalPages={payments.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
