import { Link, useNavigate } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { paymentModeLabel } from '@/features/sales/labels'
import { formatBusinessDate, formatINR } from '@/lib/format'
import { useSupplierPayments, type SupplierPayment } from './api'

const columns: Column<SupplierPayment>[] = [
  { key: 'date', header: 'Date', cell: (p) => formatBusinessDate(p.paymentDate), className: 'w-28' },
  { key: 'supplier', header: 'Supplier', cell: (p) => <span className="font-medium">{p.supplierName}</span> },
  {
    key: 'bill',
    header: 'Bill',
    cell: (p) => (
      <div className="flex flex-col">
        <Link to={`/supplier-bills/${p.supplierBillId}`} className="font-mono text-xs underline-offset-4 hover:underline" onClick={(e) => e.stopPropagation()}>
          {p.billNumber}
        </Link>
        <span className="text-xs text-muted-foreground">Their {p.supplierInvoiceNumber}</span>
      </div>
    ),
  },
  { key: 'mode', header: 'Mode', cell: (p) => paymentModeLabel(p.mode) },
  { key: 'reference', header: 'Reference', cell: (p) => <span className="font-mono text-xs">{p.reference ?? '—'}</span> },
  { key: 'by', header: 'Recorded by', cell: (p) => p.createdByName },
  { key: 'amount', header: 'Amount', align: 'right', cell: (p) => formatINR(p.amount) },
]

export function SupplierPaymentsPage() {
  const navigate = useNavigate()
  const [params, update] = useListParams()
  const payments = useSupplierPayments({ q: params.q || undefined, page: params.page, pageSize: params.pageSize })

  return (
    <>
      <PageHeader title="Supplier payments" description="Money paid to suppliers against their bills. Record a payment from the bill." />
      <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search bill, their invoice no., supplier or reference" />
      <DataTable
        columns={columns}
        rows={payments.data?.items}
        getRowKey={(p) => p.id}
        isLoading={payments.isPending}
        error={payments.error}
        onRowClick={(p) => navigate(`/supplier-bills/${p.supplierBillId}`)}
        emptyTitle={params.q ? 'No payments match' : 'No payments yet'}
        emptyDescription={params.q ? undefined : 'Payments appear here once they are recorded against supplier bills.'}
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
