import { ArrowLeft, Ban, FileCheck2, IndianRupee, Printer, Undo2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { PaidStamp } from '@/components/fx/Stamp'
import { PageHeader } from '@/components/PageHeader'
import { ProductThumb } from '@/components/ProductThumb'
import { ReasonDialog } from '@/components/ReasonDialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { RecordHistory } from '@/features/audit/RecordHistory'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { useApplyCreditNote, useAvailableCredit } from '@/features/returns/api'
import { ReturnStatusBadge } from '@/features/returns/badges'
import { creditNoteKindLabels } from '@/features/returns/labels'
import { SetCreditDialog } from '@/features/returns/SetCreditDialog'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'
import { useCancelInvoice, useInvoice, type Invoice } from './api'
import { InvoiceStatusBadge } from './badges'
import { paymentModeLabel } from './labels'
import { RecordPaymentDialog } from './RecordPaymentDialog'

function Party({ title, name, gstin, lines }: { title: string; name: string; gstin?: string | null; lines: (string | null | undefined)[] }) {
  return (
    <div className="space-y-1 text-sm">
      <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</p>
      <p className="font-medium">{name}</p>
      {lines.filter(Boolean).map((line) => (
        <p key={line} className="text-muted-foreground">
          {line}
        </p>
      ))}
      <p className="font-mono text-xs">{gstin ? `GSTIN ${gstin}` : t('Unregistered')}</p>
    </div>
  )
}

export function InvoiceDetailPage() {
  const id = Number(useParams().id)
  const { can } = useAuth()
  const canCancel = can(Permissions.InvoicesEdit)
  const canRecordPayment = can(Permissions.PaymentsEdit)
  const canCredit = can(Permissions.CreditNotesEdit)
  const invoice = useInvoice(id)
  const cancel = useCancelInvoice()
  const [paymentOpen, setPaymentOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [creditOpen, setCreditOpen] = useState(false)

  if (invoice.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (invoice.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(invoice.error)}</AlertDescription>
      </Alert>
    )
  }

  const inv = invoice.data
  const cancelled = inv.status === 'Cancelled'
  const sgstLabel = inv.usesUtgst ? 'UTGST' : 'SGST'
  const address = (a: typeof inv.billingAddress) => [a.line1, a.line2, `${a.city} - ${a.pincode}`, `${a.stateName} (${a.stateCode})`]

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <PageHeader
        title={t('Invoice {number}', { number: inv.number })}
        figures={[
          { label: t('Total'), value: formatINR(inv.grandTotal) },
          { label: t('Balance due'), value: formatINR(inv.balance), emphasis: inv.balance > 0 && !cancelled },
        ]}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <InvoiceStatusBadge status={inv.status} daysOverdue={inv.daysOverdue} settledByCredit={inv.amountPaid === 0 && inv.amountCredited > 0} />
            <span>
              <T
                text="Dated {date} · due {due} · from order {order}"
                values={{
                  date: formatBusinessDate(inv.invoiceDate),
                  due: formatBusinessDate(inv.dueDate),
                  order: (
                    <Link className="font-mono underline underline-offset-4" to={`/orders/${inv.salesOrderId}`}>
                      {inv.salesOrderNumber}
                    </Link>
                  ),
                }}
              />
            </span>
          </span>
        }
        actions={
          <>
            <Button variant="ghost" asChild>
              <Link to="/invoices">
                <ArrowLeft />
                {t('Invoices')}
              </Link>
            </Button>
            <Button variant="outline" asChild>
              <Link to={`/invoices/${inv.id}/print`} target="_blank">
                <Printer />
                {t('Print')}
              </Link>
            </Button>
            {canCancel && !cancelled && !inv.cancelBlockedReason && (
              <Button variant="outline" className="text-destructive" onClick={() => setCancelOpen(true)}>
                <Ban />
                {t('Cancel invoice')}
              </Button>
            )}
            {canCredit && !cancelled && (
              <Button variant="outline" asChild>
                <Link to={`/invoices/${inv.id}/credit-note`}>
                  <Undo2 />
                  {t('Credit note')}
                </Link>
              </Button>
            )}
            {canCredit && !cancelled && inv.balance > 0 && inv.customerCreditAvailable > 0 && (
              <Button variant="outline" onClick={() => setCreditOpen(true)}>
                <FileCheck2 />
                {t('Use credit ({amount})', { amount: formatINR(inv.customerCreditAvailable) })}
              </Button>
            )}
            {canRecordPayment && !cancelled && inv.balance > 0 && (
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
            {t('Cancelled {date}: {reason}. The number stays in the invoice register; the goods went back to stock.', {
              date: formatDate(inv.cancelledAtUtc),
              reason: inv.cancellationReason ?? '',
            })}
          </AlertDescription>
        </Alert>
      )}

      <Card className="relative">
        <PaidStamp key={inv.id} paid={inv.status === 'Paid'} className="absolute top-4 right-6 z-10" />
        <CardContent className="grid gap-6 sm:grid-cols-3">
          <Party title={t('Bill to')} name={inv.customerName} gstin={inv.customerGstin} lines={address(inv.billingAddress)} />
          {inv.shippingAddress ? (
            <Party title={t('Ship to')} name={inv.customerName} gstin={inv.customerGstin} lines={address(inv.shippingAddress)} />
          ) : (
            <div className="space-y-1 text-sm">
              <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Ship to')}</p>
              <p className="text-muted-foreground">{t('Same as billing address')}</p>
            </div>
          )}
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Tax')}</p>
            <p>{t('Place of supply: {state} ({code})', { state: inv.placeOfSupplyStateName, code: inv.placeOfSupplyStateCode })}</p>
            <p className="text-muted-foreground">
              {inv.isInterState ? t('Inter-state supply: IGST') : t('Intra-state supply: CGST + {sgst}', { sgst: sgstLabel })}
            </p>
            {inv.customerReference && <p className="text-muted-foreground">{t('Customer PO: {reference}', { reference: inv.customerReference })}</p>}
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
                  <TableHead>HSN</TableHead>
                  <TableHead className="text-right">{t('Qty')}</TableHead>
                  <TableHead className="text-right">{t('Rate')}</TableHead>
                  <TableHead className="text-right">{t('Taxable')}</TableHead>
                  <TableHead className="text-right">GST</TableHead>
                  <TableHead className="text-right">{t('Tax')}</TableHead>
                  <TableHead className="text-right">{t('Total')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {inv.lines.map((l) => (
                  <TableRow key={l.lineNumber}>
                    <TableCell>{l.lineNumber}</TableCell>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <ProductThumb productId={l.productId} name={l.productName} preview />
                        <div className="flex flex-col">
                          <span>{l.productName}</span>
                          <span className="font-mono text-xs text-muted-foreground">{l.sku}</span>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell className="font-mono text-xs">{l.hsnCode}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatQuantity(l.quantity, l.unit)}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatINR(l.unitPrice)}
                      {l.discountPercent > 0 && <span className="block text-xs text-muted-foreground">{t('less {percent}', { percent: formatPercent(l.discountPercent) })}</span>}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.taxableValue)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatPercent(l.gstRate)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.cgstAmount + l.sgstAmount + l.igstAmount)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(l.lineTotal)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
              <TableFooter>
                <TableRow>
                  <TableCell colSpan={5} className="text-right">
                    {t('Taxable value')}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatINR(inv.taxableValue)}</TableCell>
                  <TableCell colSpan={3} />
                </TableRow>
              </TableFooter>
            </Table>
          </div>

          <div className="mt-4 flex flex-wrap items-start justify-between gap-6">
            <p className="max-w-md text-sm text-muted-foreground italic">{inv.amountInWords}</p>
            <dl className="grid min-w-72 grid-cols-[1fr_auto] gap-x-8 gap-y-1.5 text-sm tabular-nums">
              <dt className="text-muted-foreground">{t('Taxable value')}</dt>
              <dd className="text-right">{formatINR(inv.taxableValue)}</dd>
              {inv.isInterState ? (
                <>
                  <dt className="text-muted-foreground">IGST</dt>
                  <dd className="text-right">{formatINR(inv.igstAmount)}</dd>
                </>
              ) : (
                <>
                  <dt className="text-muted-foreground">CGST</dt>
                  <dd className="text-right">{formatINR(inv.cgstAmount)}</dd>
                  <dt className="text-muted-foreground">{sgstLabel}</dt>
                  <dd className="text-right">{formatINR(inv.sgstAmount)}</dd>
                </>
              )}
              <dt className="text-muted-foreground">{t('Round off')}</dt>
              <dd className="text-right">{formatINR(inv.roundOff)}</dd>
              <dt className="border-t pt-1.5 font-semibold">{t('Total')}</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{formatINR(inv.grandTotal)}</dd>
              <dt className="text-muted-foreground">{t('Paid')}</dt>
              <dd className="text-right">{formatINR(inv.amountPaid)}</dd>
              {inv.amountCredited > 0 && (
                <>
                  <dt className="text-muted-foreground">{t('Credited')}</dt>
                  <dd className="text-right">{formatINR(inv.amountCredited)}</dd>
                </>
              )}
              <dt className="font-medium">{t('Balance due')}</dt>
              <dd className="text-right font-medium">{formatINR(inv.balance)}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Payments')}</CardTitle>
        </CardHeader>
        <CardContent>
          {inv.payments.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('No payments recorded yet.')}</p>
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
                  {inv.payments.map((p) => (
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

      {(inv.creditNotes.length > 0 || inv.creditsApplied.length > 0) && <CreditNotesCard invoice={inv} />}

      {!cancelled && <RecordPaymentDialog invoice={inv} open={paymentOpen} onOpenChange={setPaymentOpen} />}
      {canCredit && !cancelled && inv.balance > 0 && inv.customerCreditAvailable > 0 && (
        <UseCreditDialog invoice={inv} open={creditOpen} onOpenChange={setCreditOpen} />
      )}
      <ReasonDialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        title={t('Cancel invoice {number}?', { number: inv.number })}
        description={t('The invoice stays in the register as cancelled (GST requires it), its goods go back to stock, and the order reopens for re-invoicing.')}
        confirmLabel={t('Cancel invoice')}
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync({ id: inv.id, reason })
            toast.success(t('Invoice {number} cancelled', { number: inv.number }))
            setCancelOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />

      <RecordHistory entityType="Invoice" entityId={inv.id} />
    </div>
  )
}

/** Credit notes issued against this invoice, and credit from any of the customer's credit notes set against it. */
function CreditNotesCard({ invoice }: { invoice: Invoice }) {
  const own = new Set(invoice.creditNotes.map((c) => c.id))
  const fromOthers = invoice.creditsApplied.filter((a) => !own.has(a.creditId))
  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Credit notes')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {invoice.creditNotes.length > 0 && (
          <div className="overflow-hidden rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>{t('Credit note')}</TableHead>
                  <TableHead>{t('Date')}</TableHead>
                  <TableHead>{t('What')}</TableHead>
                  <TableHead>{t('Status')}</TableHead>
                  <TableHead className="text-right">{t('Total')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {invoice.creditNotes.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell>
                      <Link className="font-mono text-xs underline underline-offset-4" to={`/credit-notes/${c.id}`}>
                        {c.number}
                      </Link>
                    </TableCell>
                    <TableCell>{formatBusinessDate(c.date)}</TableCell>
                    <TableCell>{t(creditNoteKindLabels[c.kind as keyof typeof creditNoteKindLabels] ?? c.kind)}</TableCell>
                    <TableCell>
                      <ReturnStatusBadge status={c.status} />
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(c.grandTotal)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
        {fromOthers.length > 0 && (
          <div className="text-sm">
            <p className="mb-1 font-medium">{t('Credit from other credit notes set against this invoice')}</p>
            <ul className="space-y-1 text-muted-foreground">
              {fromOthers.map((a) => (
                <li key={a.id} className={a.reversed ? 'line-through' : undefined}>
                  <T
                    text="{amount} from {note} on {date}"
                    values={{
                      amount: formatINR(a.amount),
                      note: (
                        <Link className="font-mono underline underline-offset-4" to={`/credit-notes/${a.creditId}`}>
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
        {invoice.cancelBlockedReason && invoice.status !== 'Cancelled' && <p className="text-xs text-muted-foreground">{invoice.cancelBlockedReason}</p>}
      </CardContent>
    </Card>
  )
}

/** Sets credit from one of the customer's credit notes against what's still due on this invoice. */
function UseCreditDialog({ invoice, open, onOpenChange }: { invoice: Invoice; open: boolean; onOpenChange: (open: boolean) => void }) {
  const available = useAvailableCredit(invoice.customerId, open)
  const apply = useApplyCreditNote()
  return (
    <SetCreditDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("Use the customer's credit")}
      description={t('{customer} has {credit} of credit. {due} is due on {invoice}.', {
        customer: invoice.customerName,
        credit: formatINR(invoice.customerCreditAvailable),
        due: formatINR(invoice.balance),
        invoice: invoice.number,
      })}
      chooseLabel={t('Credit note')}
      emptyText={t('The customer has no credit left.')}
      options={available.data?.map((c) => ({
        id: c.id,
        label: t('{number} · {date} · {amount} left', { number: c.number, date: formatBusinessDate(c.date), amount: formatINR(c.available) }),
        max: Math.min(c.available, invoice.balance),
      }))}
      minDate={invoice.invoiceDate}
      onApply={async (creditNoteId, amount, date) => {
        const note = available.data!.find((c) => c.id === creditNoteId)!
        await apply.mutateAsync({ id: creditNoteId, documentId: invoice.id, amount, date, rowVersion: note.rowVersion })
        return t('{amount} from {note} set against {invoice}', { amount: formatINR(amount), note: note.number, invoice: invoice.number })
      }}
    />
  )
}
