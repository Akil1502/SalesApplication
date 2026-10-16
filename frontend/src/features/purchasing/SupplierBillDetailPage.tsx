import { ArrowLeft, Ban, CheckCircle2, IndianRupee, Sparkles, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import { ReasonDialog } from '@/components/ReasonDialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { RecordHistory } from '@/features/audit/RecordHistory'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { paymentModeLabel } from '@/features/sales/labels'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useApproveSupplierBill, useCancelSupplierBill, useSupplierBill } from './api'
import { BillStatusBadge, MatchBadge } from './badges'
import { RecordSupplierPaymentDialog } from './RecordSupplierPaymentDialog'

export function SupplierBillDetailPage() {
  const id = Number(useParams().id)
  const { can } = useAuth()
  const bill = useSupplierBill(id)
  const approve = useApproveSupplierBill()
  const cancel = useCancelSupplierBill()
  const [paymentOpen, setPaymentOpen] = useState(false)
  const [approveOpen, setApproveOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)

  if (bill.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (bill.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(bill.error)}</AlertDescription>
      </Alert>
    )
  }

  const b = bill.data
  const cancelled = b.status === 'Cancelled'
  const needsApproval = b.matchStatus === 'NeedsApproval' && !cancelled
  const lineIssues = b.lines.filter((l) => l.issue)
  const canPay = can(Permissions.SupplierPaymentsEdit) && !cancelled && !needsApproval && b.balance > 0

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <PageHeader
        title={`Bill ${b.number}`}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <MatchBadge status={b.matchStatus} />
            <BillStatusBadge status={b.status} daysOverdue={b.daysOverdue} />
            <span>
              {b.supplierName}'s invoice <span className="font-mono">{b.supplierInvoiceNumber}</span> dated {formatBusinessDate(b.billDate)} · due{' '}
              {formatBusinessDate(b.dueDate)} · for order{' '}
              <Link className="font-mono underline underline-offset-4" to={`/purchase-orders/${b.purchaseOrderId}`}>
                {b.purchaseOrderNumber}
              </Link>
            </span>
            {b.source !== 'Manual' && (
              <span className="inline-flex items-center gap-1 text-xs text-violet-700 dark:text-violet-300">
                <Sparkles className="size-3.5" /> Read by AI
              </span>
            )}
          </span>
        }
        actions={
          <>
            <Button variant="ghost" asChild>
              <Link to="/supplier-bills">
                <ArrowLeft />
                Bills
              </Link>
            </Button>
            {can(Permissions.SupplierBillsEdit) && !cancelled && b.amountPaid === 0 && (
              <Button variant="outline" className="text-destructive" onClick={() => setCancelOpen(true)}>
                <Ban />
                Cancel bill
              </Button>
            )}
            {needsApproval && can(Permissions.SupplierBillsApprove) && (
              <Button variant="outline" onClick={() => setApproveOpen(true)}>
                <CheckCircle2 />
                Approve differences
              </Button>
            )}
            {canPay && (
              <Button onClick={() => setPaymentOpen(true)}>
                <IndianRupee />
                Record payment
              </Button>
            )}
          </>
        }
      />

      {cancelled && (
        <Alert variant="destructive">
          <Ban />
          <AlertDescription>
            Cancelled {formatDate(b.cancelledAtUtc)}: {b.cancellationReason}. Its quantities count as unbilled again on the order.
          </AlertDescription>
        </Alert>
      )}

      {needsApproval && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <TriangleAlert />
          <AlertDescription className="text-inherit">
            <p className="font-medium">This bill doesn't match its order, so it can't be paid until someone who can approve bill differences accepts it.</p>
            <ul className="mt-1 ml-4 list-disc">
              {lineIssues.map((l) => (
                <li key={l.lineNumber}>
                  {l.sku}: {l.issue}
                </li>
              ))}
              {b.matchIssues.map((issue) => (
                <li key={issue}>{issue}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {b.matchStatus === 'Approved' && (
        <Alert>
          <CheckCircle2 />
          <AlertDescription>
            Differences approved by {b.approvedByName} on {formatDate(b.approvedAtUtc)}: {b.approvalNote}
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardContent className="grid gap-6 sm:grid-cols-3">
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Supplier</p>
            <Link to={`/suppliers/${b.supplierId}`} className="font-medium hover:underline">
              {b.supplierName}
            </Link>
            <p className="font-mono text-xs">{b.supplierGstin ? `GSTIN ${b.supplierGstin}` : 'Unregistered: no input credit'}</p>
            <p className="text-muted-foreground">{b.supplierStateName}</p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Tax</p>
            <p>{b.isInterState ? 'Inter-state supply: IGST' : 'Intra-state supply: CGST + SGST'}</p>
            <p className="text-muted-foreground">Input tax credit: {formatINR(b.cgstAmount + b.sgstAmount + b.igstAmount)}</p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Entered</p>
            <p>
              {b.createdByName}, {formatDate(b.createdAtUtc)}
            </p>
            {b.notes && <p className="text-muted-foreground">{b.notes}</p>}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Items</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>#</TableHead>
                  <TableHead>Product</TableHead>
                  <TableHead className="text-right">Qty</TableHead>
                  <TableHead className="text-right">Rate</TableHead>
                  <TableHead className="text-right">Order rate</TableHead>
                  <TableHead className="text-right">Taxable</TableHead>
                  <TableHead className="text-right">GST</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {b.lines.map((l) => (
                  <TableRow key={l.lineNumber} className={cn(l.issue && !cancelled && 'bg-amber-50/60 dark:bg-amber-950/30')}>
                    <TableCell>{l.lineNumber}</TableCell>
                    <TableCell>
                      <div className="flex flex-col">
                        <span>{l.productName}</span>
                        <span className="font-mono text-xs text-muted-foreground">
                          {l.sku} · HSN {l.hsnCode}
                        </span>
                        {l.issue && <span className="mt-1 text-xs text-amber-800 dark:text-amber-300">{l.issue}</span>}
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatQuantity(l.quantity, l.unit)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.unitPrice)}</TableCell>
                    <TableCell className="text-right text-muted-foreground tabular-nums">{formatINR(l.orderedPrice)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.taxableValue)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatPercent(l.gstRate)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.lineTotal)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          <div className="mt-4 flex justify-end">
            <dl className="grid min-w-72 grid-cols-[1fr_auto] gap-x-8 gap-y-1.5 text-sm tabular-nums">
              <dt className="text-muted-foreground">Goods</dt>
              <dd className="text-right">{formatINR(b.taxableValue)}</dd>
              {b.chargesAmount > 0 && (
                <>
                  <dt className="text-muted-foreground">
                    {b.chargesDescription} (GST {formatPercent(b.chargesGstRate)})
                  </dt>
                  <dd className="text-right">{formatINR(b.chargesAmount)}</dd>
                </>
              )}
              {b.isInterState ? (
                <>
                  <dt className="text-muted-foreground">IGST</dt>
                  <dd className="text-right">{formatINR(b.igstAmount)}</dd>
                </>
              ) : (
                <>
                  <dt className="text-muted-foreground">CGST</dt>
                  <dd className="text-right">{formatINR(b.cgstAmount)}</dd>
                  <dt className="text-muted-foreground">SGST</dt>
                  <dd className="text-right">{formatINR(b.sgstAmount)}</dd>
                </>
              )}
              <dt className="text-muted-foreground">Round off</dt>
              <dd className="text-right">{formatINR(b.roundOff)}</dd>
              <dt className="border-t pt-1.5 font-semibold">Total</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{formatINR(b.grandTotal)}</dd>
              {b.supplierTotal != null && b.supplierTotal !== b.grandTotal && (
                <>
                  <dt className="text-muted-foreground">On their invoice</dt>
                  <dd className="text-right">{formatINR(b.supplierTotal)}</dd>
                </>
              )}
              <dt className="text-muted-foreground">Paid</dt>
              <dd className="text-right">{formatINR(b.amountPaid)}</dd>
              <dt className="font-medium">To pay</dt>
              <dd className="text-right font-medium">{formatINR(b.balance)}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Payments</CardTitle>
        </CardHeader>
        <CardContent>
          {b.payments.length === 0 ? (
            <p className="text-sm text-muted-foreground">{needsApproval ? 'It can be paid once the differences are approved.' : 'No payments made yet.'}</p>
          ) : (
            <div className="overflow-hidden rounded-lg border">
              <Table>
                <TableHeader className="bg-muted/50">
                  <TableRow>
                    <TableHead>Date</TableHead>
                    <TableHead>Mode</TableHead>
                    <TableHead>Reference</TableHead>
                    <TableHead>Recorded by</TableHead>
                    <TableHead className="text-right">Amount</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {b.payments.map((p) => (
                    <TableRow key={p.id}>
                      <TableCell>{formatBusinessDate(p.paymentDate)}</TableCell>
                      <TableCell>{paymentModeLabel(p.mode)}</TableCell>
                      <TableCell className="font-mono text-xs">{p.reference ?? '—'}</TableCell>
                      <TableCell>{p.createdByName}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(p.amount)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      {canPay && <RecordSupplierPaymentDialog bill={b} open={paymentOpen} onOpenChange={setPaymentOpen} />}
      <ReasonDialog
        open={approveOpen}
        onOpenChange={setApproveOpen}
        title={`Approve ${b.number}?`}
        description="Accept the differences from the order, so the bill can be paid. Your note is kept with the bill and in the audit log."
        label="Why the differences are acceptable"
        requiredMessage="Say why the differences are acceptable."
        cancelLabel="Not now"
        confirmLabel="Approve"
        destructive={false}
        onConfirm={async (note) => {
          try {
            await approve.mutateAsync({ id: b.id, rowVersion: b.rowVersion, note })
            toast.success(`${b.number} approved; it can be paid now`)
            setApproveOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />
      <ReasonDialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        title={`Cancel bill ${b.number}?`}
        description="It stays on record as cancelled, its quantities count as unbilled on the order, and its price differences and charges come back out of the products' cost."
        confirmLabel="Cancel bill"
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync({ id: b.id, reason })
            toast.success(`Bill ${b.number} cancelled`)
            setCancelOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />

      <RecordHistory entityType="Supplier bill" entityId={b.id} />
    </div>
  )
}
