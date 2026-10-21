import { useState } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { formatQuantity, todayInIndia } from '@/lib/format'
import { useReceiveGoods, type PurchaseOrder } from './api'
import { t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'

interface ReceiveGoodsDialogProps {
  order: PurchaseOrder
  open: boolean
  onOpenChange: (open: boolean) => void
}

/** Records what arrived against an order: a goods receipt (GRN) that puts the goods into stock at the order's price. */
export function ReceiveGoodsDialog({ order, open, onOpenChange }: ReceiveGoodsDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{t('Receive goods')}</DialogTitle>
          <DialogDescription>
            {t(
              'Against {order} from {supplier}. Enter what you accepted, and anything refused at the door: rejected goods never enter stock and stay due on the order.',
              { order: order.number, supplier: order.supplierName },
            )}
          </DialogDescription>
        </DialogHeader>
        {open && <ReceiveForm order={order} onDone={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  )
}

function ReceiveForm({ order, onDone }: { order: PurchaseOrder; onDone: () => void }) {
  const receive = useReceiveGoods()
  const lines = order.lines.filter((l) => l.maxReceivable > 0)
  // Each line starts at what's still to come.
  const [quantities, setQuantities] = useState<Record<number, string>>(() => Object.fromEntries(lines.map((l) => [l.id, l.quantityPending > 0 ? String(l.quantityPending) : ''])))
  const [rejected, setRejected] = useState<Record<number, string>>({})
  const [reasons, setReasons] = useState<Record<number, string>>({})
  const [receiptDate, setReceiptDate] = useState(todayInIndia())
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [received, setReceived] = useState<{ id: number; number: string } | null>(null)

  const problem = (lineId: number, max: number): string | null => {
    const raw = (quantities[lineId] ?? '').trim()
    if (raw === '') return null
    if (!/^\d+(\.\d{1,3})?$/.test(raw)) return t('Up to 3 decimals.')
    if (Number(raw) > max) return t('At most {quantity}.', { quantity: formatQuantity(max) })
    return null
  }
  const rejectProblem = (lineId: number): string | null => {
    const raw = (rejected[lineId] ?? '').trim()
    if (raw === '') return null
    if (!/^\d+(\.\d{1,3})?$/.test(raw)) return t('Up to 3 decimals.')
    return Number(raw) > 0 && !(reasons[lineId] ?? '').trim() ? t('Say why.') : null
  }
  const entered = lines.filter((l) => Number(quantities[l.id]) > 0 || Number(rejected[l.id]) > 0)
  const invalid = lines.some((l) => problem(l.id, l.maxReceivable) !== null || rejectProblem(l.id) !== null)

  const submit = async () => {
    setErrors({})
    try {
      const receipt = await receive.mutateAsync({
        id: order.id,
        body: {
          rowVersion: order.rowVersion,
          receiptDate,
          supplierReference: reference.trim() || null,
          notes: notes.trim() || null,
          lines: entered.map((l) => ({
            purchaseOrderLineId: l.id,
            quantity: Number(quantities[l.id]) || 0,
            quantityRejected: Number(rejected[l.id]) || 0,
            rejectionReason: Number(rejected[l.id]) > 0 ? (reasons[l.id] ?? '').trim() || null : null,
          })),
        },
      })
      toast.success(t('Goods receipt {number} recorded; stock updated', { number: receipt.number }))
      setReceived({ id: receipt.id, number: receipt.number })
    } catch (error) {
      if (error instanceof ApiRequestError && error.body.errors) {
        setErrors(Object.fromEntries(Object.entries(error.body.errors).map(([key, messages]) => [key, messages[0] ?? ''])))
      }
      toast.error(errorMessage(error))
    }
  }

  if (received) {
    return (
      <div className="flex flex-col gap-4 py-2">
        <p className="text-sm">
          <T
            text="Recorded as {receipt}. The accepted goods are in stock, and the products' cost has been re-averaged at the order price. Anything rejected is still due on the order."
            values={{
              receipt: (
                <Link to={`/stock/receipts/${received.id}`} className="font-mono underline underline-offset-4">
                  {received.number}
                </Link>
              ),
            }}
          />
        </p>
        <DialogFooter>
          <Button type="button" onClick={onDone}>
            {t('Done')}
          </Button>
        </DialogFooter>
      </div>
    )
  }

  return (
    <>
      <FieldGroup className="grid gap-4 py-2 sm:grid-cols-3">
        <Field data-invalid={!!errors.receiptDate}>
          <FieldLabel htmlFor="receipt-date">{t('Received on')}</FieldLabel>
          <Input id="receipt-date" type="date" value={receiptDate} min={order.orderDate} max={todayInIndia()} onChange={(e) => setReceiptDate(e.target.value)} />
          <FieldError errors={errors.receiptDate ? [{ message: errors.receiptDate }] : []} />
        </Field>
        <Field>
          <FieldLabel htmlFor="receipt-ref">{t('Delivery challan / invoice no.')}</FieldLabel>
          <Input id="receipt-ref" value={reference} maxLength={50} placeholder={t('Optional')} onChange={(e) => setReference(e.target.value)} />
        </Field>
        <Field>
          <FieldLabel htmlFor="receipt-notes">{t('Notes')}</FieldLabel>
          <Input id="receipt-notes" value={notes} maxLength={1000} placeholder={t('Optional')} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </FieldGroup>

      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader className="bg-muted/50">
            <TableRow>
              <TableHead>{t('Product')}</TableHead>
              <TableHead className="text-right">{t('Ordered')}</TableHead>
              <TableHead className="text-right">{t('Received')}</TableHead>
              <TableHead className="text-right">{t('Still to come')}</TableHead>
              <TableHead className="w-32">{t('Accepted')}</TableHead>
              <TableHead className="w-44">{t('Rejected')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {lines.map((l, index) => {
              const lineProblem = problem(l.id, l.maxReceivable) ?? errors[`lines[${entered.indexOf(l)}].quantity`]
              return (
                <TableRow key={l.id} className="align-top hover:bg-transparent">
                  <TableCell>
                    <span className="font-mono text-xs">{l.sku}</span>
                    <span className="block text-sm">{l.productName}</span>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatQuantity(l.quantity, l.unit)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatQuantity(l.quantityReceived)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatQuantity(l.quantityPending)}</TableCell>
                  <TableCell>
                    <Field data-invalid={!!lineProblem}>
                      <Input
                        value={quantities[l.id] ?? ''}
                        inputMode="decimal"
                        placeholder="0"
                        aria-label={t('Quantity of {sku} received, line {line}', { sku: l.sku, line: index + 1 })}
                        aria-invalid={!!lineProblem}
                        onChange={(e) => setQuantities((current) => ({ ...current, [l.id]: e.target.value }))}
                      />
                      <FieldError errors={lineProblem ? [{ message: lineProblem }] : []} />
                    </Field>
                  </TableCell>
                  <TableCell>
                    <Field data-invalid={!!(rejectProblem(l.id) ?? errors[`lines[${entered.indexOf(l)}].rejectionReason`])}>
                      <Input
                        value={rejected[l.id] ?? ''}
                        inputMode="decimal"
                        placeholder="0"
                        aria-label={t('Quantity of {sku} rejected, line {line}', { sku: l.sku, line: index + 1 })}
                        onChange={(e) => setRejected((current) => ({ ...current, [l.id]: e.target.value }))}
                      />
                      {Number(rejected[l.id]) > 0 && (
                        <Input
                          value={reasons[l.id] ?? ''}
                          maxLength={200}
                          placeholder={t('Why, e.g. wet cones')}
                          aria-label={t('Why {sku} was rejected', { sku: l.sku })}
                          onChange={(e) => setReasons((current) => ({ ...current, [l.id]: e.target.value }))}
                        />
                      )}
                      <FieldError
                        errors={
                          (rejectProblem(l.id) ?? errors[`lines[${entered.indexOf(l)}].rejectionReason`])
                            ? [{ message: rejectProblem(l.id) ?? errors[`lines[${entered.indexOf(l)}].rejectionReason`] }]
                            : []
                        }
                      />
                    </Field>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </div>
      <p className="text-xs text-muted-foreground">{t('A little more than ordered is accepted, up to the over-receipt tolerance in Company settings.')}</p>

      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="button" disabled={entered.length === 0 || invalid || receive.isPending} onClick={submit}>
          {receive.isPending && <Spinner />}
          {t('Record receipt')}
        </Button>
      </DialogFooter>
    </>
  )
}
