import { useId, useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { errorMessage } from '@/lib/api/errors'
import { formatINR, todayInIndia } from '@/lib/format'
import { t } from '@/lib/i18n'
import { useCreateInvoice, type Order } from './api'

/** Issues the invoice for a confirmed order. GST is recalculated at today's rates and stock is deducted. */
export function CreateInvoiceDialog({ order, open, onOpenChange }: { order: Order; open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* Content unmounts on close, so the date and any error reset each time it opens. */}
      <DialogContent>
        <CreateInvoiceForm order={order} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function CreateInvoiceForm({ order, onDone }: { order: Order; onDone: () => void }) {
  const id = useId()
  const navigate = useNavigate()
  const create = useCreateInvoice()
  const [date, setDate] = useState(todayInIndia())
  const [error, setError] = useState<string | null>(null)

  const shortLines = order.lines.filter((l) => l.quantity > l.quantityOnHand)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    // Dialogs render in a portal, but React still bubbles events to the parent component tree.
    e.stopPropagation()
    setError(null)
    try {
      const invoice = await create.mutateAsync({ salesOrderId: order.id, invoiceDate: date })
      toast.success(t('Invoice {number} issued', { number: invoice.number }), { description: formatINR(invoice.grandTotal) })
      onDone()
      navigate(`/invoices/${invoice.id}`)
    } catch (err) {
      setError(errorMessage(err))
    }
  }

  return (
    <form onSubmit={submit} noValidate>
      <DialogHeader>
        <DialogTitle>{t('Issue invoice for {number}', { number: order.number })}</DialogTitle>
        <DialogDescription>
          {t(
            "The invoice gets the next number for its financial year, uses today's GST rates, and takes the goods out of stock. It can't be edited afterwards, only cancelled.",
          )}
        </DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-4 py-4">
        {shortLines.length > 0 && (
          <Alert variant="destructive">
            <AlertDescription>
              {t('Not enough stock for: {lines}. Receive stock first.', {
                lines: shortLines.map((l) => t('{sku} (need {need}, have {have})', { sku: l.sku, need: l.quantity, have: l.quantityOnHand })).join(', '),
              })}
            </AlertDescription>
          </Alert>
        )}
        {error && (
          <Alert variant="destructive" role="alert">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <Field>
          <FieldLabel htmlFor={id}>{t('Invoice date')}</FieldLabel>
          <Input id={id} type="date" value={date} max={todayInIndia()} min={order.orderDate} onChange={(e) => setDate(e.target.value)} />
          <FieldDescription>{t('Invoices are numbered in date order within the financial year.')}</FieldDescription>
        </Field>
      </div>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={create.isPending}>
          {create.isPending && <Spinner />}
          {t('Issue invoice')}
        </Button>
      </DialogFooter>
    </form>
  )
}
