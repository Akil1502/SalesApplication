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
      .regex(/^\d+(\.\d{1,2})?$/, { error: 'Enter an amount like 5000 or 5000.50.' })
      .refine((v) => Number(v) > 0, { error: 'More than zero.' })
      .refine((v) => Number(v) <= bill.balance, { error: `Only ${formatINR(bill.balance)} is due.` }),
    mode: z.string().min(1),
    paymentDate: z.string().min(1, { error: 'Choose a date.' }),
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
      toast.success(`${formatINR(payment.amount)} paid against ${bill.number}`)
      onDone()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['amount', 'paymentDate', 'reference', 'notes'])) toast.error(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>Record payment to supplier</DialogTitle>
        <DialogDescription>
          {bill.number} · {bill.supplierName} (their {bill.supplierInvoiceNumber}) · {formatINR(bill.balance)} to pay
        </DialogDescription>
      </DialogHeader>
      <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
        <FormInput control={form.control} name="amount" label="Amount (₹)" inputMode="decimal" autoFocus />
        <FormSelect control={form.control} name="mode" label="Mode" options={paymentModes} />
        <FormInput control={form.control} name="paymentDate" label="Paid on" type="date" max={todayInIndia()} min={bill.billDate} />
        <FormInput
          control={form.control}
          name="reference"
          label={mode === 'Cheque' ? 'Cheque number' : mode === 'Cash' ? 'Voucher number' : 'UTR / transaction ID'}
          placeholder="Optional"
        />
        <FormInput control={form.control} name="notes" label="Notes" placeholder="Optional" className="sm:col-span-2" />
      </FieldGroup>
      {mode === 'Cash' && (
        <p className="pb-2 text-xs text-muted-foreground">Cash to one supplier is limited to ₹10,000 a day (Income-tax Act s.40A(3)).</p>
      )}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          Record payment
        </Button>
      </DialogFooter>
    </form>
  )
}
