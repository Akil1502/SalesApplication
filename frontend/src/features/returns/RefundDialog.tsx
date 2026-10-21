import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSelect } from '@/components/form/fields'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import type { PaymentMode } from '@/features/sales/api'
import { paymentModes } from '@/features/sales/labels'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatINR, todayInIndia } from '@/lib/format'
import { t } from '@/lib/i18n'
import type { RefundCreateRequest } from './api'

interface RefundDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  dateLabel: string
  available: number
  minDate: string
  rowVersion: string
  onRefund: (body: RefundCreateRequest) => Promise<unknown>
}

/** Records credit paid back: to a customer from a credit note, or to us by a supplier for returned goods. */
export function RefundDialog(props: RefundDialogProps) {
  return (
    <Dialog open={props.open} onOpenChange={props.onOpenChange}>
      <DialogContent>{props.open && <RefundForm {...props} />}</DialogContent>
    </Dialog>
  )
}

function RefundForm({ title, description, dateLabel, available, minDate, rowVersion, onRefund, onOpenChange }: RefundDialogProps) {
  const schema = z.object({
    amount: z
      .string()
      .trim()
      .regex(/^\d+(\.\d{1,2})?$/, { error: t('Enter an amount like 5000 or 5000.50.') })
      .refine((v) => Number(v) > 0, { error: t('More than zero.') })
      .refine((v) => Number(v) <= available, { error: t('Only {amount} of credit is left.', { amount: formatINR(available) }) }),
    mode: z.string().min(1),
    refundDate: z.string().min(1, { error: t('Choose a date.') }),
    reference: z.string().trim().max(50),
    notes: z.string().trim().max(500),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { amount: available.toFixed(2), mode: 'Neft', refundDate: todayInIndia(), reference: '', notes: '' },
  })
  const mode = useWatch({ control: form.control, name: 'mode' })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await onRefund({
        amount: Number(values.amount),
        mode: values.mode as PaymentMode,
        refundDate: values.refundDate,
        reference: values.reference || null,
        notes: values.notes || null,
        rowVersion,
      })
      toast.success(t('Refund of {amount} recorded', { amount: formatINR(Number(values.amount)) }))
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['amount', 'refundDate', 'reference', 'notes'])) toast.error(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
      <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
        <FormInput control={form.control} name="amount" label={t('Amount (₹)')} inputMode="decimal" autoFocus />
        <FormSelect control={form.control} name="mode" label={t('Mode')} options={paymentModes.map((m) => ({ ...m, label: t(m.label) }))} />
        <FormInput control={form.control} name="refundDate" label={dateLabel} type="date" min={minDate} max={todayInIndia()} />
        <FormInput
          control={form.control}
          name="reference"
          label={mode === 'Cheque' ? t('Cheque number') : mode === 'Cash' ? t('Voucher number') : t('UTR / transaction ID')}
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
          {t('Record refund')}
        </Button>
      </DialogFooter>
    </form>
  )
}
