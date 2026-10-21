import { ArrowLeft, Ban, FileCheck2, FilePen, Printer, Undo2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import { ReasonDialog } from '@/components/ReasonDialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { RecordHistory } from '@/features/audit/RecordHistory'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { useSupplierBills } from '@/features/purchasing/api'
import { paymentModeLabel } from '@/features/sales/labels'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity, todayInIndia } from '@/lib/format'
import { t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'
import { useApplySupplierCredit, useCancelSupplierReturn, useNoteSupplierCreditNote, useRefundSupplierReturn, useSupplierReturn, type SupplierReturn } from './api'
import { CreditLeftBadge, NoGstBadge, ReturnStatusBadge } from './badges'
import { returnReasonLabels } from './labels'
import { RefundDialog } from './RefundDialog'
import { SetCreditDialog } from './SetCreditDialog'

export function SupplierReturnDetailPage() {
  const id = Number(useParams().id)
  const { can } = useAuth()
  const canEdit = can(Permissions.SupplierReturnsEdit)
  const ret = useSupplierReturn(id)
  const cancel = useCancelSupplierReturn()
  const refund = useRefundSupplierReturn()
  const [cancelOpen, setCancelOpen] = useState(false)
  const [applyOpen, setApplyOpen] = useState(false)
  const [refundOpen, setRefundOpen] = useState(false)
  const [noteOpen, setNoteOpen] = useState(false)

  if (ret.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (ret.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(ret.error)}</AlertDescription>
      </Alert>
    )
  }

  const r = ret.data
  const cancelled = r.status === 'Cancelled'

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <PageHeader
        title={t('Debit note {number}', { number: r.number })}
        figures={[
          { label: t('Total'), value: formatINR(r.grandTotal) },
          { label: t('Credit left'), value: formatINR(r.creditAvailable), emphasis: r.creditAvailable > 0 },
        ]}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <ReturnStatusBadge status={r.status} />
            {!r.withGst && <NoGstBadge />}
            {!cancelled && <CreditLeftBadge amount={r.creditAvailable} />}
            <span>
              <T
                text="Goods returned to {supplier} · {reason} · {date} · against bill {bill}"
                values={{
                  supplier: r.supplierName,
                  reason: t(returnReasonLabels[r.reason]),
                  date: formatBusinessDate(r.returnDate),
                  bill: <BillLink id={r.supplierBillId} number={r.billNumber} />,
                }}
              />
            </span>
          </span>
        }
        actions={
          <>
            <Button variant="ghost" asChild>
              <Link to="/supplier-returns">
                <ArrowLeft />
                {t('Supplier returns')}
              </Link>
            </Button>
            <Button variant="outline" asChild>
              <Link to={`/supplier-returns/${r.id}/print`} target="_blank">
                <Printer />
                {t('Print')}
              </Link>
            </Button>
            {canEdit && !cancelled && (
              <Button variant="outline" onClick={() => setNoteOpen(true)}>
                <FilePen />
                {r.supplierCreditNoteNumber ? t('Change their credit note') : t('Note their credit note')}
              </Button>
            )}
            {canEdit && !r.cancelBlockedReason && !cancelled && (
              <Button variant="outline" className="text-destructive" onClick={() => setCancelOpen(true)}>
                <Ban />
                {t('Cancel return')}
              </Button>
            )}
            {canEdit && !cancelled && r.creditAvailable > 0 && (
              <>
                <Button variant="outline" onClick={() => setRefundOpen(true)}>
                  <Undo2 />
                  {t('Record refund')}
                </Button>
                <Button onClick={() => setApplyOpen(true)}>
                  <FileCheck2 />
                  {t('Set against a bill')}
                </Button>
              </>
            )}
          </>
        }
      />

      {cancelled && (
        <Alert variant="destructive">
          <Ban />
          <AlertDescription>
            {t('Cancelled {date}: {reason}. The goods came back into stock and the bill owes its full amount again.', {
              date: formatDate(r.cancelledAtUtc),
              reason: r.cancellationReason ?? '',
            })}
          </AlertDescription>
        </Alert>
      )}

      {!cancelled && !r.supplierCreditNoteNumber && (
        <Alert>
          <FilePen />
          <AlertDescription>
            {t("Waiting for {supplier}'s credit note for these goods. Note its number here when it arrives.", { supplier: r.supplierName })}
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardContent className="grid gap-6 sm:grid-cols-3">
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Supplier')}</p>
            <Link to={`/suppliers/${r.supplierId}`} className="font-medium hover:underline">
              {r.supplierName}
            </Link>
            <p className="font-mono text-xs">{r.supplierGstin ? `GSTIN ${r.supplierGstin}` : t('Unregistered')}</p>
            <p className="text-muted-foreground">{r.supplierStateName}</p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Against')}</p>
            <p>
              <T
                text="Bill {bill} (their {number}, {date})"
                values={{ bill: <BillLink id={r.supplierBillId} number={r.billNumber} />, number: r.supplierInvoiceNumber, date: formatBusinessDate(r.billDate) }}
              />
            </p>
            <p className="text-muted-foreground">
              <T
                text="Order {number}"
                values={{
                  number: (
                    <Link className="font-mono underline underline-offset-4" to={`/purchase-orders/${r.purchaseOrderId}`}>
                      {r.purchaseOrderNumber}
                    </Link>
                  ),
                }}
              />
            </p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Their credit note')}</p>
            <p>
              {!r.supplierCreditNoteNumber
                ? t('Awaited')
                : r.supplierCreditNoteDate
                  ? t('{number} of {date}', { number: r.supplierCreditNoteNumber, date: formatBusinessDate(r.supplierCreditNoteDate) })
                  : r.supplierCreditNoteNumber}
            </p>
            {r.reasonNote && <p className="text-muted-foreground">{r.reasonNote}</p>}
            <p className="text-muted-foreground">
              {t('Recorded by {name}, {date}', { name: r.createdByName, date: formatDate(r.createdAtUtc) })}
            </p>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Goods returned')}</CardTitle>
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
                  <TableHead className="text-right">{t('Taxable')}</TableHead>
                  <TableHead className="text-right">GST</TableHead>
                  <TableHead className="text-right">{t('Total')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {r.lines.map((l) => (
                  <TableRow key={l.lineNumber}>
                    <TableCell>{l.lineNumber}</TableCell>
                    <TableCell>
                      <span className="block">{l.productName}</span>
                      <span className="font-mono text-xs text-muted-foreground">
                        {l.sku} · HSN {l.hsnCode}
                      </span>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatQuantity(l.quantity, l.unit)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.unitPrice)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.taxableValue)}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.withGst ? formatPercent(l.gstRate) : '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.lineTotal)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          <div className="mt-4 flex flex-wrap items-start justify-between gap-6">
            <p className="max-w-md text-sm text-muted-foreground italic">{r.amountInWords}</p>
            <dl className="grid min-w-72 grid-cols-[1fr_auto] gap-x-8 gap-y-1.5 text-sm tabular-nums">
              <dt className="text-muted-foreground">{t('Taxable value')}</dt>
              <dd className="text-right">{formatINR(r.taxableValue)}</dd>
              {r.isInterState ? (
                <>
                  <dt className="text-muted-foreground">{t('IGST reversed')}</dt>
                  <dd className="text-right">{formatINR(r.igstAmount)}</dd>
                </>
              ) : (
                <>
                  <dt className="text-muted-foreground">{t('CGST reversed')}</dt>
                  <dd className="text-right">{formatINR(r.cgstAmount)}</dd>
                  <dt className="text-muted-foreground">{t('SGST reversed')}</dt>
                  <dd className="text-right">{formatINR(r.sgstAmount)}</dd>
                </>
              )}
              <dt className="text-muted-foreground">{t('Round off')}</dt>
              <dd className="text-right">{formatINR(r.roundOff)}</dd>
              <dt className="border-t pt-1.5 font-semibold">{t('Total')}</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{formatINR(r.grandTotal)}</dd>
              <dt className="text-muted-foreground">{t('Set against bills')}</dt>
              <dd className="text-right">{formatINR(r.amountAdjusted)}</dd>
              <dt className="text-muted-foreground">{t('Refunded to us')}</dt>
              <dd className="text-right">{formatINR(r.amountRefunded)}</dd>
              <dt className="font-medium">{t('Credit left')}</dt>
              <dd className="text-right font-medium">{formatINR(r.creditAvailable)}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Where the credit went')}</CardTitle>
        </CardHeader>
        <CardContent>
          {r.adjustments.length === 0 && r.refunds.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t("Not used yet. Set it against one of {supplier}'s bills, or record their refund.", { supplier: r.supplierName })}
            </p>
          ) : (
            <div className="overflow-hidden rounded-lg border">
              <Table>
                <TableHeader className="bg-muted/50">
                  <TableRow>
                    <TableHead>{t('Date')}</TableHead>
                    <TableHead>{t('Used for')}</TableHead>
                    <TableHead>{t('Recorded by')}</TableHead>
                    <TableHead className="text-right">{t('Amount')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {r.adjustments.map((a) => (
                    <TableRow key={`a${a.id}`} className={a.reversed ? 'text-muted-foreground line-through' : undefined}>
                      <TableCell>{formatBusinessDate(a.date)}</TableCell>
                      <TableCell>
                        {a.reversed ? (
                          <T text="Set against {number} (reversed)" values={{ number: <BillLink id={a.documentId} number={a.documentNumber} /> }} />
                        ) : (
                          <T text="Set against {number}" values={{ number: <BillLink id={a.documentId} number={a.documentNumber} /> }} />
                        )}
                      </TableCell>
                      <TableCell>{a.createdByName}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(a.amount)}</TableCell>
                    </TableRow>
                  ))}
                  {r.refunds.map((f) => (
                    <TableRow key={`r${f.id}`}>
                      <TableCell>{formatBusinessDate(f.refundDate)}</TableCell>
                      <TableCell>
                        {t('Refunded to us by {mode}', { mode: t(paymentModeLabel(f.mode)) })}
                        {f.reference && <span className="font-mono text-xs"> · {f.reference}</span>}
                      </TableCell>
                      <TableCell>{f.createdByName}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(f.amount)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      {canEdit && !cancelled && r.creditAvailable > 0 && (
        <>
          <ApplyToBillDialog ret={r} open={applyOpen} onOpenChange={setApplyOpen} />
          <RefundDialog
            open={refundOpen}
            onOpenChange={setRefundOpen}
            title={t("Record the supplier's refund")}
            description={t('{number} · {supplier} · {amount} of credit left', { number: r.number, supplier: r.supplierName, amount: formatINR(r.creditAvailable) })}
            dateLabel={t('Received on')}
            available={r.creditAvailable}
            minDate={r.returnDate}
            rowVersion={r.rowVersion}
            onRefund={(body) => refund.mutateAsync({ id: r.id, body })}
          />
        </>
      )}
      {canEdit && !cancelled && <SupplierNoteDialog ret={r} open={noteOpen} onOpenChange={setNoteOpen} />}
      <ReasonDialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        title={t('Cancel debit note {number}?', { number: r.number })}
        description={t('It stays on record as cancelled. The goods come back into stock at the price they went out at, and the bill owes its full amount again.')}
        confirmLabel={t('Cancel return')}
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync({ id: r.id, reason, rowVersion: r.rowVersion })
            toast.success(t('Debit note {number} cancelled', { number: r.number }))
            setCancelOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />

      <RecordHistory entityType="Supplier return" entityId={r.id} />
    </div>
  )
}

function BillLink({ id, number }: { id: number; number: string }) {
  return (
    <Link className="font-mono underline underline-offset-4" to={`/supplier-bills/${id}`}>
      {number}
    </Link>
  )
}

function ApplyToBillDialog({ ret, open, onOpenChange }: { ret: SupplierReturn; open: boolean; onOpenChange: (open: boolean) => void }) {
  const apply = useApplySupplierCredit()
  const bills = useSupplierBills({ supplierId: ret.supplierId, unpaidOnly: true, page: 1, pageSize: 100 })
  const openBills = bills.data?.items.filter((b) => b.balance > 0)
  return (
    <SetCreditDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t('Set credit against a bill')}
      description={t('{number} has {amount} of credit left with {supplier}.', { number: ret.number, amount: formatINR(ret.creditAvailable), supplier: ret.supplierName })}
      chooseLabel={t('Bill')}
      emptyText={t('Nothing is owed to {supplier} right now. Keep the credit for their next bill, or record their refund.', { supplier: ret.supplierName })}
      options={openBills?.map((b) => ({
        id: b.id,
        label: t('{number} (their {theirs}) · {amount} owed', { number: b.number, theirs: b.supplierInvoiceNumber, amount: formatINR(b.balance) }),
        max: Math.min(b.balance, ret.creditAvailable),
      }))}
      minDate={ret.returnDate}
      onApply={async (billId, amount, date) => {
        await apply.mutateAsync({ id: ret.id, documentId: billId, amount, date, rowVersion: ret.rowVersion })
        return t('{amount} of credit set against {number}', { amount: formatINR(amount), number: openBills?.find((b) => b.id === billId)?.number ?? '' })
      }}
    />
  )
}

function SupplierNoteDialog({ ret, open, onOpenChange }: { ret: SupplierReturn; open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>{open && <SupplierNoteForm ret={ret} onDone={() => onOpenChange(false)} />}</DialogContent>
    </Dialog>
  )
}

function SupplierNoteForm({ ret, onDone }: { ret: SupplierReturn; onDone: () => void }) {
  const note = useNoteSupplierCreditNote()
  const [number, setNumber] = useState(ret.supplierCreditNoteNumber ?? '')
  const [date, setDate] = useState(ret.supplierCreditNoteDate ?? todayInIndia())
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (!number.trim()) {
      setError(t("Enter the supplier's credit note number."))
      return
    }

    try {
      await note.mutateAsync({ id: ret.id, supplierCreditNoteNumber: number.trim(), supplierCreditNoteDate: date || null, rowVersion: ret.rowVersion })
      toast.success(t("{supplier}'s credit note {number} noted", { supplier: ret.supplierName, number: number.trim() }))
      onDone()
    } catch (e) {
      toast.error(errorMessage(e))
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t("{supplier}'s credit note", { supplier: ret.supplierName })}</DialogTitle>
        <DialogDescription>
          {t('The credit note they sent for the goods on {number}, so the input GST reversal can be matched to it.', { number: ret.number })}
        </DialogDescription>
      </DialogHeader>
      <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
        <Field data-invalid={!!error}>
          <FieldLabel htmlFor="their-cn">{t('Their credit note no.')}</FieldLabel>
          <Input id="their-cn" value={number} maxLength={30} autoFocus onChange={(e) => setNumber(e.target.value)} />
          <FieldError errors={error ? [{ message: error }] : []} />
        </Field>
        <Field>
          <FieldLabel htmlFor="their-cn-date">{t('Dated')}</FieldLabel>
          <Input id="their-cn-date" type="date" value={date} max={todayInIndia()} onChange={(e) => setDate(e.target.value)} />
        </Field>
      </FieldGroup>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="button" disabled={note.isPending} onClick={submit}>
          {note.isPending && <Spinner />}
          {t('Save')}
        </Button>
      </DialogFooter>
    </>
  )
}
