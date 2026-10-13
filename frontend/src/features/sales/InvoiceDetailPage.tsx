import { ArrowLeft, Ban, IndianRupee, Printer } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import { ReasonDialog } from '@/components/ReasonDialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Roles, useAuth } from '@/features/auth/auth-context'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { useCancelInvoice, useInvoice } from './api'
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
      <p className="font-mono text-xs">{gstin ? `GSTIN ${gstin}` : 'Unregistered'}</p>
    </div>
  )
}

export function InvoiceDetailPage() {
  const id = Number(useParams().id)
  const { hasRole } = useAuth()
  const isManager = hasRole(Roles.Admin, Roles.SalesManager)
  const invoice = useInvoice(id)
  const cancel = useCancelInvoice()
  const [paymentOpen, setPaymentOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)

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
        title={`Invoice ${inv.number}`}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            <InvoiceStatusBadge status={inv.status} daysOverdue={inv.daysOverdue} />
            <span>
              Dated {formatBusinessDate(inv.invoiceDate)} · due {formatBusinessDate(inv.dueDate)} · from order{' '}
              <Link className="font-mono underline underline-offset-4" to={`/orders/${inv.salesOrderId}`}>
                {inv.salesOrderNumber}
              </Link>
            </span>
          </span>
        }
        actions={
          <>
            <Button variant="ghost" asChild>
              <Link to="/invoices">
                <ArrowLeft />
                Invoices
              </Link>
            </Button>
            <Button variant="outline" asChild>
              <Link to={`/invoices/${inv.id}/print`} target="_blank">
                <Printer />
                Print
              </Link>
            </Button>
            {isManager && !cancelled && inv.amountPaid === 0 && (
              <Button variant="outline" className="text-destructive" onClick={() => setCancelOpen(true)}>
                <Ban />
                Cancel invoice
              </Button>
            )}
            {isManager && !cancelled && inv.balance > 0 && (
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
            Cancelled {formatDate(inv.cancelledAtUtc)}: {inv.cancellationReason}. The number stays in the invoice register; the goods went back to stock.
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardContent className="grid gap-6 sm:grid-cols-3">
          <Party title="Bill to" name={inv.customerName} gstin={inv.customerGstin} lines={address(inv.billingAddress)} />
          {inv.shippingAddress ? (
            <Party title="Ship to" name={inv.customerName} gstin={inv.customerGstin} lines={address(inv.shippingAddress)} />
          ) : (
            <div className="space-y-1 text-sm">
              <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Ship to</p>
              <p className="text-muted-foreground">Same as billing address</p>
            </div>
          )}
          <div className="space-y-1 text-sm">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Tax</p>
            <p>
              Place of supply: {inv.placeOfSupplyStateName} ({inv.placeOfSupplyStateCode})
            </p>
            <p className="text-muted-foreground">{inv.isInterState ? 'Inter-state supply: IGST' : `Intra-state supply: CGST + ${sgstLabel}`}</p>
            {inv.customerReference && <p className="text-muted-foreground">Customer PO: {inv.customerReference}</p>}
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
                  <TableHead>HSN</TableHead>
                  <TableHead className="text-right">Qty</TableHead>
                  <TableHead className="text-right">Rate</TableHead>
                  <TableHead className="text-right">Taxable</TableHead>
                  <TableHead className="text-right">GST</TableHead>
                  <TableHead className="text-right">Tax</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {inv.lines.map((l) => (
                  <TableRow key={l.lineNumber}>
                    <TableCell>{l.lineNumber}</TableCell>
                    <TableCell>
                      <div className="flex flex-col">
                        <span>{l.productName}</span>
                        <span className="font-mono text-xs text-muted-foreground">{l.sku}</span>
                      </div>
                    </TableCell>
                    <TableCell className="font-mono text-xs">{l.hsnCode}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatQuantity(l.quantity, l.unit)}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatINR(l.unitPrice)}
                      {l.discountPercent > 0 && <span className="block text-xs text-muted-foreground">less {formatPercent(l.discountPercent)}</span>}
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
                    Taxable value
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
              <dt className="text-muted-foreground">Taxable value</dt>
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
              <dt className="text-muted-foreground">Round off</dt>
              <dd className="text-right">{formatINR(inv.roundOff)}</dd>
              <dt className="border-t pt-1.5 font-semibold">Total</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{formatINR(inv.grandTotal)}</dd>
              <dt className="text-muted-foreground">Paid</dt>
              <dd className="text-right">{formatINR(inv.amountPaid)}</dd>
              <dt className="font-medium">Balance due</dt>
              <dd className="text-right font-medium">{formatINR(inv.balance)}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Payments</CardTitle>
        </CardHeader>
        <CardContent>
          {inv.payments.length === 0 ? (
            <p className="text-sm text-muted-foreground">No payments recorded yet.</p>
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

      {!cancelled && <RecordPaymentDialog invoice={inv} open={paymentOpen} onOpenChange={setPaymentOpen} />}
      <ReasonDialog
        open={cancelOpen}
        onOpenChange={setCancelOpen}
        title={`Cancel invoice ${inv.number}?`}
        description="The invoice stays in the register as cancelled (GST requires it), its goods go back to stock, and the order reopens for re-invoicing."
        confirmLabel="Cancel invoice"
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync({ id: inv.id, reason })
            toast.success(`Invoice ${inv.number} cancelled`)
            setCancelOpen(false)
          } catch (error) {
            toast.error(errorMessage(error))
          }
        }}
      />
    </div>
  )
}
