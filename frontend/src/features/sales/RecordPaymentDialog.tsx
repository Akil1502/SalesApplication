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
import { t } from '@/lib/i18n'
import { useRecordPayment, type Invoice, type PaymentMode } from './api'
import { paymentModes } from './labels'

interface RecordPaymentDialogProps {
  /** A full invoice or a list row: only these are needed. */
  invoice: Pick<Invoice, 'id' | 'number' | 'customerName' | 'balance' | 'invoiceDate'>
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function RecordPaymentDialog({ invoice, open, onOpenChange }: RecordPaymentDialogProps) {
  const record = useRecordPayment()
  const schema = z.object({
    amount: z
      .string()
      .trim()
      .regex(/^\d+(\.\d{1,2})?$/, { error: t('Enter an amount like 5000 or 5000.50.') })
      .refine((v) => Number(v) > 0, { error: t('More than zero.') })
      .refine((v) => Number(v) <= invoice.balance, { error: t('Only {amount} is due.', { amount: formatINR(invoice.balance) }) }),
    mode: z.string().min(1),
    paymentDate: z.string().min(1, { error: t('Choose a date.') }),
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
      toast.success(t('{amount} recorded against {invoice}', { amount: formatINR(payment.amount), invoice: invoice.number }))
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
            <DialogTitle>{t('Record payment')}</DialogTitle>
            <DialogDescription>
              {t('{invoice} · {customer} · {amount} due', { invoice: invoice.number, customer: invoice.customerName, amount: formatINR(invoice.balance) })}
            </DialogDescription>
          </DialogHeader>
          <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
            <FormInput control={form.control} name="amount" label={t('Amount (₹)')} inputMode="decimal" autoFocus />
            <FormSelect control={form.control} name="mode" label={t('Mode')} options={paymentModes} />
            <FormInput control={form.control} name="paymentDate" label={t('Received on')} type="date" max={todayInIndia()} min={invoice.invoiceDate} />
            <FormInput
              control={form.control}
              name="reference"
              label={mode === 'Cheque' ? t('Cheque number') : mode === 'Cash' ? t('Receipt number') : t('UTR / transaction ID')}
              placeholder={t('Optional')}
            />
            <FormInput control={form.control} name="notes" label={t('Notes')} placeholder={t('Optional')} className="sm:col-span-2" />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              {t('Record payment')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
