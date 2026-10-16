import { ArrowLeft, Ban, FileCheck2, Printer, Undo2 } from 'lucide-react'
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
import { useInvoices } from '@/features/sales/api'
import { paymentModeLabel } from '@/features/sales/labels'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { useApplyCreditNote, useCancelCreditNote, useCreditNote, useRefundCreditNote, type CreditNote } from './api'
import { CreditLeftBadge, NoGstBadge, ReturnStatusBadge } from './badges'
import { creditNoteKindLabels, dispositionLabels, returnReasonLabels } from './labels'
import { RefundDialog } from './RefundDialog'
import { SetCreditDialog } from './SetCreditDialog'

export function CreditNoteDetailPage() {
  const id = Number(useParams().id)
  const { can } = useAuth()
  const canEdit = can(Permissions.CreditNotesEdit)
  const note = useCreditNote(id)
  const cancel = useCancelCreditNote()
  const refund = useRefundCreditNote()
  const [cancelOpen, setCancelOpen] = useState(false)
  const [applyOpen, setApplyOpen] = useState(false)
  const [refundOpen, setRefundOpen] = useState(false)

  if (note.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (note.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(note.error)}</AlertDescription>
      </Alert>
    )
  }

  const cn = note.data
  const cancelled = cn.status === 'Cancelled'
  const sgst = cn.usesUtgst ? 'UTGST' : 'SGST'
  const goods = cn.kind === 'GoodsReturn'

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <PageHeader
        title={`Credit note ${cn.number}`}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <ReturnStatusBadge status={cn.status} />
            {!cn.withGst && <NoGstBadge />}
            {!cancelled && <CreditLeftBadge amount={cn.creditAvailable} />}
            <span>
              {creditNoteKindLabels[cn.kind]} · {returnReasonLabels[cn.reason]} · dated {formatBusinessDate(cn.creditNoteDate)} · against{' '}
              <Link className="font-mono underline underline-offset-4" to={`/invoices/${cn.invoiceId}`}>
                {cn.invoiceNumber}
              </Link>
            </span>
          </span>
        }
        actions={
          <>
            <Button variant="ghost" asChild>
              <Link to="/credit-notes">
                <ArrowLeft />
                Credit notes
              </Link>
            </Button>
            <Button variant="outline" asChild>
              <Link to={`/credit-notes/${cn.id}/print`} target="_blank">
                <Printer />
                Print
              </Link>
            </Button>
            {canEdit && !cn.cancelBlockedReason && !cancelled && (
              <Button variant="outline" className="text-destructive" onClick={() => setCancelOpen(true)}>
                <Ban />
                Cancel credit note
              </Button>
            )}
            {canEdit && !cancelled && cn.creditAvailable > 0 && (
              <>
                <Button variant="outline" onClick={() => setRefundOpen(true)}>
                  <Undo2 />
                  Record refund
                </Button>
                <Button onClick={() => setApplyOpen(true)}>
                  <FileCheck2 />
                  Set against an invoice
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
            Cancelled {formatDate(cn.cancelledAtUtc)}: {cn.cancellationReason}. The number stays in the register; goods that had gone back into stock came
            out again and the invoice owes its full amount again.
          </AlertDescription>
        </Alert>
      )}

      {!cn.withGst && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <AlertDescription className="text-inherit">
            Issued after the GST time limit for {cn.invoiceNumber} (30 November after its financial year), so it credits the value only. It doesn't
            reduce GST and the GST reports leave it out.
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardContent className="grid gap-6 sm:grid-cols-3">
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Customer</p>
            <Link to={`/customers/${cn.customerId}`} className="font-medium hover:underline">
              {cn.customerName}
            </Link>
            <p className="text-muted-foreground">
              {cn.billingAddress.city}, {cn.billingAddress.stateName}
            </p>
            <p className="font-mono text-xs">{cn.customerGstin ? `GSTIN ${cn.customerGstin}` : 'Unregistered'}</p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Against</p>
            <p>
              Invoice{' '}
              <Link className="font-mono underline underline-offset-4" to={`/invoices/${cn.invoiceId}`}>
                {cn.invoiceNumber}
              </Link>{' '}
              of {formatBusinessDate(cn.invoiceDate)}
            </p>
            <p className="text-muted-foreground">
              {cn.isInterState ? 'Inter-state: IGST' : `Intra-state: CGST + ${sgst}`} · place of supply {cn.placeOfSupplyStateName}
            </p>
          </div>
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Issued</p>
            <p>
              {cn.createdByName}, {formatDate(cn.createdAtUtc)}
            </p>
            {cn.reasonNote && <p className="text-muted-foreground">{cn.reasonNote}</p>}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{goods ? 'Goods returned' : 'Value credited'}</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>#</TableHead>
                  <TableHead>Product</TableHead>
                  {goods && <TableHead className="text-right">Qty</TableHead>}
                  {goods && <TableHead>Goods</TableHead>}
                  <TableHead className="text-right">Taxable</TableHead>
                  <TableHead className="text-right">GST</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {cn.lines.map((l) => (
                  <TableRow key={l.lineNumber}>
                    <TableCell>{l.lineNumber}</TableCell>
                    <TableCell>
                      <span className="block">{l.productName}</span>
                      <span className="font-mono text-xs text-muted-foreground">
                        {l.sku} · HSN {l.hsnCode}
                      </span>
                    </TableCell>
                    {goods && <TableCell className="text-right tabular-nums">{l.quantity != null ? formatQuantity(l.quantity, l.unit) : '—'}</TableCell>}
                    {goods && <TableCell>{l.disposition ? dispositionLabels[l.disposition] : '—'}</TableCell>}
                    <TableCell className="text-right tabular-nums">{formatINR(l.taxableValue)}</TableCell>
                    <TableCell className="text-right tabular-nums">{cn.withGst ? formatPercent(l.gstRate) : '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.lineTotal)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          <div className="mt-4 flex flex-wrap items-start justify-between gap-6">
            <p className="max-w-md text-sm text-muted-foreground italic">{cn.amountInWords}</p>
            <dl className="grid min-w-72 grid-cols-[1fr_auto] gap-x-8 gap-y-1.5 text-sm tabular-nums">
              <dt className="text-muted-foreground">Taxable value</dt>
              <dd className="text-right">{formatINR(cn.taxableValue)}</dd>
              {cn.isInterState ? (
                <>
                  <dt className="text-muted-foreground">IGST</dt>
                  <dd className="text-right">{formatINR(cn.igstAmount)}</dd>
                </>
              ) : (
                <>
                  <dt className="text-muted-foreground">CGST</dt>
                  <dd className="text-right">{formatINR(cn.cgstAmount)}</dd>
                  <dt className="text-muted-foreground">{sgst}</dt>
                  <dd className="text-right">{formatINR(cn.sgstAmount)}</dd>
                </>
              )}
              <dt className="text-muted-foreground">Round off</dt>
              <dd className="text-right">{formatINR(cn.roundOff)}</dd>
              <dt className="border-t pt-1.5 font-semibold">Total</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{formatINR(cn.grandTotal)}</dd>
              <dt className="text-muted-foreground">Set against invoices</dt>
              <dd className="text-right">{formatINR(cn.amountAdjusted)}</dd>
              <dt className="text-muted-foreground">Refunded</dt>
              <dd className="text-right">{formatINR(cn.amountRefunded)}</dd>
              <dt className="font-medium">Credit left</dt>
              <dd className="text-right font-medium">{formatINR(cn.creditAvailable)}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <CreditUseCard note={cn} />

      {canEdit && !cancelled && cn.creditAvailable > 0 && (
        <>
          <ApplyToInvoiceDialog note={cn} open={applyOpen} onOpenChange={setApplyOpen} />
          <RefundDialog
            open={refundOpen}
            onOpenChange={setRefundOpen}
            title="Refund credit to the customer"
            description={`${cn.number} · ${cn.customerName} · ${formatINR(cn.creditAvailable)} of credit left`}
            dateLabel="Refunded on"
            available={cn.creditAvailable}
            minDate={cn.creditNoteDate}
            rowVersion={cn.rowVersion}
            onRefund={(body) => refund.mutateAsync({ id: cn.id, body })}
          />
        </>
      )}
      <ReasonDialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        title={`Cancel credit note ${cn.number}?`}
        description="It stays in the register as cancelled. Goods that went back into stock come out again, and its credit comes back off the invoice."
        confirmLabel="Cancel credit note"
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync({ id: cn.id, reason, rowVersion: cn.rowVersion })
            toast.success(`Credit note ${cn.number} cancelled`)
            setCancelOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />

      <RecordHistory entityType="Credit note" entityId={cn.id} />
    </div>
  )
}

/** Where the credit went: invoices it was set against, and refunds. */
function CreditUseCard({ note }: { note: CreditNote }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Where the credit went</CardTitle>
      </CardHeader>
      <CardContent>
        {note.adjustments.length === 0 && note.refunds.length === 0 ? (
          <p className="text-sm text-muted-foreground">Not used yet. Set it against one of the customer's invoices, or record a refund.</p>
        ) : (
          <div className="overflow-hidden rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Used for</TableHead>
                  <TableHead>Recorded by</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {note.adjustments.map((a) => (
                  <TableRow key={`a${a.id}`} className={a.reversed ? 'text-muted-foreground line-through' : undefined}>
                    <TableCell>{formatBusinessDate(a.date)}</TableCell>
                    <TableCell>
                      Set against{' '}
                      <Link className="font-mono underline underline-offset-4" to={`/invoices/${a.documentId}`}>
                        {a.documentNumber}
                      </Link>
                      {a.reversed && ' (reversed)'}
                    </TableCell>
                    <TableCell>{a.createdByName}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(a.amount)}</TableCell>
                  </TableRow>
                ))}
                {note.refunds.map((r) => (
                  <TableRow key={`r${r.id}`}>
                    <TableCell>{formatBusinessDate(r.refundDate)}</TableCell>
                    <TableCell>
                      Refunded by {paymentModeLabel(r.mode)}
                      {r.reference && <span className="font-mono text-xs"> · {r.reference}</span>}
                    </TableCell>
                    <TableCell>{r.createdByName}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(r.amount)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function ApplyToInvoiceDialog({ note, open, onOpenChange }: { note: CreditNote; open: boolean; onOpenChange: (open: boolean) => void }) {
  const apply = useApplyCreditNote()
  // The customer's invoices with something still due.
  const invoices = useInvoices({ customerId: note.customerId, page: 1, pageSize: 100 })
  const open_ = invoices.data?.items.filter((i) => i.status !== 'Cancelled' && i.balance > 0)
  return (
    <SetCreditDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Set credit against an invoice"
      description={`${note.number} has ${formatINR(note.creditAvailable)} of credit left for ${note.customerName}.`}
      chooseLabel="Invoice"
      emptyText={`${note.customerName} has no invoices with anything due. Record a refund instead, or keep the credit for their next invoice.`}
      options={open_?.map((i) => ({ id: i.id, label: `${i.number} · ${formatBusinessDate(i.invoiceDate)} · ${formatINR(i.balance)} due`, max: Math.min(i.balance, note.creditAvailable) }))}
      minDate={note.creditNoteDate}
      onApply={async (invoiceId, amount, date) => {
        await apply.mutateAsync({ id: note.id, documentId: invoiceId, amount, date, rowVersion: note.rowVersion })
        return `${formatINR(amount)} of credit set against ${open_?.find((i) => i.id === invoiceId)?.number}`
      }}
    />
  )
}
