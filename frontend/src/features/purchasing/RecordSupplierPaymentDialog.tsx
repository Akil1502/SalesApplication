import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSelect } from '@/components/form/fields'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { paymentModes } from '@/features/sales/labels'
import type { PaymentMode } from '@/features/sales/api'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatINR, todayInIndia } from '@/lib/format'
import { useRecordSupplierPayment, type SupplierBill } from './api'
import { t } from '@/lib/i18n'

interface RecordSupplierPaymentDialogProps {
  bill: SupplierBill
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function RecordSupplierPaymentDialog({ bill, open, onOpenChange }: RecordSupplierPaymentDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>{open && <PaymentForm bill={bill} onDone={() => onOpenChange(false)} />}</DialogContent>
    </Dialog>
  )
}

function PaymentForm({ bill, onDone }: { bill: SupplierBill; onDone: () => void }) {
  const record = useRecordSupplierPayment()
  const schema = z.object({
    amount: z
      .string()
      .trim()
      .regex(/^\d+(\.\d{1,2})?$/, { error: t('Enter an amount like 5000 or 5000.50.') })
      .refine((v) => Number(v) > 0, { error: t('More than zero.') })
      .refine((v) => Number(v) <= bill.balance, { error: t('Only {amount} is due.', { amount: formatINR(bill.balance) }) }),
    mode: z.string().min(1),
    paymentDate: z.string().min(1, { error: t('Choose a date.') }),
    reference: z.string().trim().max(50),
    notes: z.string().trim().max(500),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { amount: bill.balance.toFixed(2), mode: bill.balance >= 200_000 ? 'Rtgs' : 'Neft', paymentDate: todayInIndia(), reference: '', notes: '' },
  })
  const mode = useWatch({ control: form.control, name: 'mode' })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const payment = await record.mutateAsync({
        supplierBillId: bill.id,
        amount: Number(values.amount),
        mode: values.mode as PaymentMode,
        paymentDate: values.paymentDate,
        reference: values.reference || null,
        notes: values.notes || null,
      })
      toast.success(t('{amount} paid against {bill}', { amount: formatINR(payment.amount), bill: bill.number }))
      onDone()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['amount', 'paymentDate', 'reference', 'notes'])) toast.error(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>{t('Record payment to supplier')}</DialogTitle>
        <DialogDescription>
          {t('{bill} · {supplier} (their {invoice}) · {amount} to pay', {
            bill: bill.number,
            supplier: bill.supplierName,
            invoice: bill.supplierInvoiceNumber,
            amount: formatINR(bill.balance),
          })}
        </DialogDescription>
      </DialogHeader>
      <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
        <FormInput control={form.control} name="amount" label={t('Amount (₹)')} inputMode="decimal" autoFocus />
        <FormSelect control={form.control} name="mode" label={t('Mode')} options={paymentModes.map((m) => ({ ...m, label: t(m.label) }))} />
        <FormInput control={form.control} name="paymentDate" label={t('Paid on')} type="date" max={todayInIndia()} min={bill.billDate} />
        <FormInput
          control={form.control}
          name="reference"
          label={mode === 'Cheque' ? t('Cheque number') : mode === 'Cash' ? t('Voucher number') : t('UTR / transaction ID')}
          placeholder={t('Optional')}
        />
        <FormInput control={form.control} name="notes" label={t('Notes')} placeholder={t('Optional')} className="sm:col-span-2" />
      </FieldGroup>
      {mode === 'Cash' && (
        <p className="pb-2 text-xs text-muted-foreground">{t('Cash to one supplier is limited to ₹10,000 a day (Income-tax Act s.40A(3)).')}</p>
      )}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {t('Record payment')}
        </Button>
      </DialogFooter>
    </form>
  )
}
