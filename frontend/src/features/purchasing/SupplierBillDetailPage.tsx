import { ArrowLeft, Ban, CheckCircle2, FileCheck2, IndianRupee, Sparkles, TriangleAlert, Undo2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { PaidStamp } from '@/components/fx/Stamp'
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
import { useApplySupplierCredit, useAvailableSupplierCredit } from '@/features/returns/api'
import { ReturnStatusBadge } from '@/features/returns/badges'
import { returnReasonLabels } from '@/features/returns/labels'
import { SetCreditDialog } from '@/features/returns/SetCreditDialog'
import { paymentModeLabel } from '@/features/sales/labels'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useApproveSupplierBill, useCancelSupplierBill, useSupplierBill, type SupplierBill } from './api'
import { BillStatusBadge, MatchBadge } from './badges'
import { RecordSupplierPaymentDialog } from './RecordSupplierPaymentDialog'
import { t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'

export function SupplierBillDetailPage() {
  const id = Number(useParams().id)
  const { can } = useAuth()
  const bill = useSupplierBill(id)
  const approve = useApproveSupplierBill()
  const cancel = useCancelSupplierBill()
  const [paymentOpen, setPaymentOpen] = useState(false)
  const [approveOpen, setApproveOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [creditOpen, setCreditOpen] = useState(false)

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
  const canReturn = can(Permissions.SupplierReturnsEdit) && !cancelled
  const canUseCredit = canReturn && b.balance > 0 && b.supplierCreditAvailable > 0

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <PageHeader
        title={t('Bill {number}', { number: b.number })}
        figures={[
          { label: t('Total'), value: formatINR(b.grandTotal) },
          { label: t('Balance'), value: formatINR(b.balance), emphasis: b.balance > 0 && !cancelled },
        ]}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <MatchBadge status={b.matchStatus} />
            <BillStatusBadge status={b.status} daysOverdue={b.daysOverdue} />
            <span>
              <T
                text="{supplier}'s invoice {invoice} dated {date} · due {due} · for order {order}"
                values={{
                  supplier: b.supplierName,
                  invoice: <span className="font-mono">{b.supplierInvoiceNumber}</span>,
                  date: formatBusinessDate(b.billDate),
                  due: formatBusinessDate(b.dueDate),
                  order: (
                    <Link className="font-mono underline underline-offset-4" to={`/purchase-orders/${b.purchaseOrderId}`}>
                      {b.purchaseOrderNumber}
                    </Link>
                  ),
                }}
              />
            </span>
            {b.source !== 'Manual' && (
              <span className="inline-flex items-center gap-1 text-xs text-violet-700 dark:text-violet-300">
                <Sparkles className="size-3.5" /> {t('Read by AI')}
              </span>
            )}
          </span>
        }
        actions={
          <>
            <Button variant="ghost" asChild>
              <Link to="/supplier-bills">
                <ArrowLeft />
                {t('Bills')}
              </Link>
            </Button>
            {can(Permissions.SupplierBillsEdit) && !cancelled && !b.cancelBlockedReason && (
              <Button variant="outline" className="text-destructive" onClick={() => setCancelOpen(true)}>
                <Ban />
                {t('Cancel bill')}
              </Button>
            )}
            {canReturn && b.lines.some((l) => l.quantity > l.quantityReturned) && (
              <Button variant="outline" asChild>
                <Link to={`/supplier-bills/${b.id}/return`}>
                  <Undo2 />
                  {t('Return goods')}
                </Link>
              </Button>
            )}
            {canUseCredit && (
              <Button variant="outline" onClick={() => setCreditOpen(true)}>
                <FileCheck2 />
                {t('Use credit ({amount})', { amount: formatINR(b.supplierCreditAvailable) })}
              </Button>
            )}
            {needsApproval && can(Permissions.SupplierBillsApprove) && (
              <Button variant="outline" onClick={() => setApproveOpen(true)}>
                <CheckCircle2 />
                {t('Approve differences')}
              </Button>
            )}
            {canPay && (
              <Button onClick={() => setPaymentOpen(true)}>
                <IndianRupee />
                {t('Record payment')}
              </Button>
            )}
          </>
        }
      />

      {cancelled && (
        <Alert variant="destructive">
          <Ban />
          <AlertDescription>
            {t('Cancelled {date}: {reason}. Its quantities count as unbilled again on the order.', {
              date: formatDate(b.cancelledAtUtc),
              reason: b.cancellationReason ?? '',
            })}
          </AlertDescription>
        </Alert>
      )}

      {needsApproval && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <TriangleAlert />
          <AlertDescription className="text-inherit">
            <p className="font-medium">
              {t("This bill doesn't match its order, so it can't be paid until someone who can approve bill differences accepts it.")}
            </p>
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
            {t('Differences approved by {name} on {date}: {note}', {
              name: b.approvedByName ?? '',
              date: formatDate(b.approvedAtUtc),
              note: b.approvalNote ?? '',
            })}
          </AlertDescription>
        </Alert>
      )}

      <Card className="relative">
        <PaidStamp key={b.id} paid={b.status === 'Paid'} className="absolute top-4 right-6 z-10" />
        <CardContent className="grid gap-6 sm:grid-cols-3">
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Supplier')}</p>
            <Link to={`/suppliers/${b.supplierId}`} className="font-medium hover:underline">
              {b.supplierName}
            </Link>
            <p className="font-mono text-xs">{b.supplierGstin ? `GSTIN ${b.supplierGstin}` : t('Unregistered: no input credit')}</p>
            <p className="text-muted-foreground">{b.supplierStateName}</p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Tax')}</p>
            <p>{b.isInterState ? t('Inter-state supply: IGST') : t('Intra-state supply: CGST + SGST')}</p>
            <p className="text-muted-foreground">{t('Input tax credit: {amount}', { amount: formatINR(b.cgstAmount + b.sgstAmount + b.igstAmount) })}</p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Entered')}</p>
            <p>
              {b.createdByName}, {formatDate(b.createdAtUtc)}
            </p>
            {b.notes && <p className="text-muted-foreground">{b.notes}</p>}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Items')}</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-lg border">
            <Table stack>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>#</TableHead>
                  <TableHead>{t('Product')}</TableHead>
                  <TableHead className="text-right">{t('Qty')}</TableHead>
                  <TableHead className="text-right">{t('Rate')}</TableHead>
                  <TableHead className="text-right">{t('Order rate')}</TableHead>
                  <TableHead className="text-right">{t('Taxable')}</TableHead>
                  <TableHead className="text-right">GST</TableHead>
                  <TableHead className="text-right">{t('Total')}</TableHead>
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
                    <TableCell className="text-right tabular-nums">
                      {formatQuantity(l.quantity, l.unit)}
                      {l.quantityReturned > 0 && (
                        <span className="block text-xs text-muted-foreground">{t('{quantity} returned', { quantity: formatQuantity(l.quantityReturned) })}</span>
                      )}
                    </TableCell>
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
              <dt className="text-muted-foreground">{t('Goods')}</dt>
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
              <dt className="text-muted-foreground">{t('Round off')}</dt>
              <dd className="text-right">{formatINR(b.roundOff)}</dd>
              <dt className="border-t pt-1.5 font-semibold">{t('Total')}</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{formatINR(b.grandTotal)}</dd>
              {b.supplierTotal != null && b.supplierTotal !== b.grandTotal && (
                <>
                  <dt className="text-muted-foreground">{t('On their invoice')}</dt>
                  <dd className="text-right">{formatINR(b.supplierTotal)}</dd>
                </>
              )}
              <dt className="text-muted-foreground">{t('Paid')}</dt>
              <dd className="text-right">{formatINR(b.amountPaid)}</dd>
              {b.amountCredited > 0 && (
                <>
                  <dt className="text-muted-foreground">{t('Goods returned')}</dt>
                  <dd className="text-right">{formatINR(b.amountCredited)}</dd>
                </>
              )}
              <dt className="font-medium">{t('To pay')}</dt>
              <dd className="text-right font-medium">{formatINR(b.balance)}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Payments')}</CardTitle>
        </CardHeader>
        <CardContent>
          {b.payments.length === 0 ? (
            <p className="text-sm text-muted-foreground">{needsApproval ? t('It can be paid once the differences are approved.') : t('No payments made yet.')}</p>
          ) : (
            <div className="overflow-hidden rounded-lg border">
              <Table>
                <TableHeader className="bg-muted/50">
                  <TableRow>
                    <TableHead>{t('Date')}</TableHead>
                    <TableHead>{t('Mode')}</TableHead>
                    <TableHead>{t('Reference')}</TableHead>
                    <TableHead>{t('Recorded by')}</TableHead>
                    <TableHead className="text-right">{t('Amount')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {b.payments.map((p) => (
                    <TableRow key={p.id}>
                      <TableCell>{formatBusinessDate(p.paymentDate)}</TableCell>
                      <TableCell>{t(paymentModeLabel(p.mode))}</TableCell>
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

      {(b.returns.length > 0 || b.creditsApplied.length > 0) && <ReturnsCard bill={b} />}

      {canPay && <RecordSupplierPaymentDialog bill={b} open={paymentOpen} onOpenChange={setPaymentOpen} />}
      {canUseCredit && <UseSupplierCreditDialog bill={b} open={creditOpen} onOpenChange={setCreditOpen} />}
      <ReasonDialog
        open={approveOpen}
        onOpenChange={setApproveOpen}
        title={t('Approve {number}?', { number: b.number })}
        description={t('Accept the differences from the order, so the bill can be paid. Your note is kept with the bill and in the audit log.')}
        label={t('Why the differences are acceptable')}
        requiredMessage={t('Say why the differences are acceptable.')}
        cancelLabel={t('Not now')}
        confirmLabel={t('Approve')}
        destructive={false}
        onConfirm={async (note) => {
          try {
            await approve.mutateAsync({ id: b.id, rowVersion: b.rowVersion, note })
            toast.success(t('{number} approved; it can be paid now', { number: b.number }))
            setApproveOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />
      <ReasonDialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        title={t('Cancel bill {number}?', { number: b.number })}
        description={t("It stays on record as cancelled, its quantities count as unbilled on the order, and its price differences and charges come back out of the products' cost.")}
        confirmLabel={t('Cancel bill')}
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync({ id: b.id, reason })
            toast.success(t('Bill {number} cancelled', { number: b.number }))
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

/** Goods returned against this bill (our debit notes), and credit from other returns set against it. */
function ReturnsCard({ bill }: { bill: SupplierBill }) {
  const own = new Set(bill.returns.map((r) => r.id))
  const fromOthers = bill.creditsApplied.filter((a) => !own.has(a.creditId))
  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Goods returned')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {bill.returns.length > 0 && (
          <div className="overflow-hidden rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>{t('Debit note')}</TableHead>
                  <TableHead>{t('Date')}</TableHead>
                  <TableHead>{t('Reason')}</TableHead>
                  <TableHead>{t('Status')}</TableHead>
                  <TableHead className="text-right">{t('Total')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {bill.returns.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell>
                      <Link className="font-mono text-xs underline underline-offset-4" to={`/supplier-returns/${r.id}`}>
                        {r.number}
                      </Link>
                    </TableCell>
                    <TableCell>{formatBusinessDate(r.date)}</TableCell>
                    <TableCell>{t(returnReasonLabels[r.reason])}</TableCell>
                    <TableCell>
                      <ReturnStatusBadge status={r.status} />
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(r.grandTotal)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
        {fromOthers.length > 0 && (
          <div className="text-sm">
            <p className="mb-1 font-medium">{t('Credit from other returns set against this bill')}</p>
            <ul className="space-y-1 text-muted-foreground">
              {fromOthers.map((a) => (
                <li key={a.id} className={a.reversed ? 'line-through' : undefined}>
                  <T
                    text="{amount} from {credit} on {date}"
                    values={{
                      amount: formatINR(a.amount),
                      credit: (
                        <Link className="font-mono underline underline-offset-4" to={`/supplier-returns/${a.creditId}`}>
                          {a.creditNumber}
                        </Link>
                      ),
                      date: formatBusinessDate(a.date),
                    }}
                  />
                </li>
              ))}
            </ul>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/** Sets credit we have with the supplier (from earlier returns) against what we owe on this bill. */
function UseSupplierCreditDialog({ bill, open, onOpenChange }: { bill: SupplierBill; open: boolean; onOpenChange: (open: boolean) => void }) {
  const available = useAvailableSupplierCredit(bill.supplierId, open)
  const apply = useApplySupplierCredit()
  return (
    <SetCreditDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t('Use credit with the supplier')}
      description={t('We have {credit} of credit with {supplier}. {owed} is owed on {bill}.', {
        credit: formatINR(bill.supplierCreditAvailable),
        supplier: bill.supplierName,
        owed: formatINR(bill.balance),
        bill: bill.number,
      })}
      chooseLabel={t('Supplier return')}
      emptyText={t('No credit is left with this supplier.')}
      options={available.data?.map((r) => ({
        id: r.id,
        label: t('{number} · {date} · {amount} left', { number: r.number, date: formatBusinessDate(r.date), amount: formatINR(r.available) }),
        max: Math.min(r.available, bill.balance),
      }))}
      minDate={bill.billDate}
      onApply={async (returnId, amount, date) => {
        const ret = available.data!.find((r) => r.id === returnId)!
        await apply.mutateAsync({ id: returnId, documentId: bill.id, amount, date, rowVersion: ret.rowVersion })
        return t('{amount} from {credit} set against {bill}', { amount: formatINR(amount), credit: ret.number, bill: bill.number })
      }}
    />
  )
}
