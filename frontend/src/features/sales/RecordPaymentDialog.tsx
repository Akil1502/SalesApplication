import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSelect } from '@/components/form/fields'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatINR, todayInIndia } from '@/lib/format'
import { useRecordPayment, type Invoice, type PaymentMode } from './api'
import { paymentModes } from './labels'

interface RecordPaymentDialogProps {
  invoice: Invoice
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function RecordPaymentDialog({ invoice, open, onOpenChange }: RecordPaymentDialogProps) {
  const record = useRecordPayment()
  const schema = z.object({
    amount: z
      .string()
      .trim()
      .regex(/^\d+(\.\d{1,2})?$/, { error: 'Enter an amount like 5000 or 5000.50.' })
      .refine((v) => Number(v) > 0, { error: 'More than zero.' })
      .refine((v) => Number(v) <= invoice.balance, { error: `Only ${formatINR(invoice.balance)} is due.` }),
    mode: z.string().min(1),
    paymentDate: z.string().min(1, { error: 'Choose a date.' }),
    reference: z.string().trim().max(50),
    notes: z.string().trim().max(500),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (open) {
      form.reset({ amount: invoice.balance.toFixed(2), mode: 'Upi', paymentDate: todayInIndia(), reference: '', notes: '' })
    }
  }, [open, invoice.balance, form])

  const mode = useWatch({ control: form.control, name: 'mode' })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const payment = await record.mutateAsync({
        invoiceId: invoice.id,
        amount: Number(values.amount),
        mode: values.mode as PaymentMode,
        paymentDate: values.paymentDate,
        reference: values.reference || null,
        notes: values.notes || null,
      })
      toast.success(`${formatINR(payment.amount)} recorded against ${invoice.number}`)
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['amount', 'paymentDate', 'reference', 'notes'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>Record payment</DialogTitle>
            <DialogDescription>
              {invoice.number} · {invoice.customerName} · {formatINR(invoice.balance)} due
            </DialogDescription>
          </DialogHeader>
          <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
            <FormInput control={form.control} name="amount" label="Amount (₹)" inputMode="decimal" autoFocus />
            <FormSelect control={form.control} name="mode" label="Mode" options={paymentModes} />
            <FormInput control={form.control} name="paymentDate" label="Received on" type="date" max={todayInIndia()} min={invoice.invoiceDate} />
            <FormInput
              control={form.control}
              name="reference"
              label={mode === 'Cheque' ? 'Cheque number' : mode === 'Cash' ? 'Receipt number' : 'UTR / transaction ID'}
              placeholder="Optional"
            />
            <FormInput control={form.control} name="notes" label="Notes" placeholder="Optional" className="sm:col-span-2" />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              Record payment
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
