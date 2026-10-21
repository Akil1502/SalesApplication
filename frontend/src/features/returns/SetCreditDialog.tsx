import { zodResolver } from '@hookform/resolvers/zod'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput } from '@/components/form/fields'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatINR, todayInIndia } from '@/lib/format'
import { t } from '@/lib/i18n'

/** Something credit can be set against (an invoice or bill), or a credit to use (a credit note or return). */
export interface CreditOption {
  id: number
  label: string
  /** The most that can go against it: the smaller of what's due and the credit left. */
  max: number
}

interface SetCreditDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  chooseLabel: string
  options: CreditOption[] | undefined
  emptyText: string
  minDate: string
  onApply: (optionId: number, amount: number, date: string) => Promise<string>
}

/** Sets credit from a credit note or supplier return against an invoice or bill, part or all of it. */
export function SetCreditDialog(props: SetCreditDialogProps) {
  return (
    <Dialog open={props.open} onOpenChange={props.onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{props.title}</DialogTitle>
          <DialogDescription>{props.description}</DialogDescription>
        </DialogHeader>
        {props.open &&
          (props.options === undefined ? (
            <Skeleton className="h-32 w-full" />
          ) : props.options.length === 0 ? (
            <p className="py-4 text-sm text-muted-foreground">{props.emptyText}</p>
          ) : (
            <CreditForm {...props} options={props.options} />
          ))}
      </DialogContent>
    </Dialog>
  )
}

function CreditForm({ options, chooseLabel, minDate, onApply, onOpenChange }: SetCreditDialogProps & { options: CreditOption[] }) {
  const first = options[0]!
  const schema = z
    .object({
      optionId: z.string().min(1, { error: t('Choose one.') }),
      amount: z
        .string()
        .trim()
        .regex(/^\d+(\.\d{1,2})?$/, { error: t('Enter an amount like 5000 or 5000.50.') })
        .refine((v) => Number(v) > 0, { error: t('More than zero.') }),
      date: z.string().min(1, { error: t('Choose a date.') }),
    })
    .superRefine((v, ctx) => {
      const max = options.find((o) => String(o.id) === v.optionId)?.max ?? 0
      if (Number(v.amount) > max) ctx.addIssue({ code: 'custom', path: ['amount'], message: t('At most {amount}.', { amount: formatINR(max) }) })
    })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { optionId: String(first.id), amount: first.max.toFixed(2), date: todayInIndia() },
  })
  const optionId = useWatch({ control: form.control, name: 'optionId' })
  const chosen = options.find((o) => String(o.id) === optionId)

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const message = await onApply(Number(values.optionId), Number(values.amount), values.date)
      toast.success(message)
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['amount', 'date'])) toast.error(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
        <Controller
          control={form.control}
          name="optionId"
          render={({ field, fieldState }) => (
            <Field data-invalid={fieldState.invalid} className="sm:col-span-2">
              <FieldLabel htmlFor="credit-option">{chooseLabel}</FieldLabel>
              <Select
                value={field.value}
                onValueChange={(value) => {
                  field.onChange(value)
                  // Start from the most that can go against the new choice.
                  const option = options.find((o) => String(o.id) === value)
                  if (option) form.setValue('amount', option.max.toFixed(2))
                }}
              >
                <SelectTrigger id="credit-option" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {options.map((o) => (
                    <SelectItem key={o.id} value={String(o.id)}>
                      {o.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FieldError errors={[fieldState.error]} />
            </Field>
          )}
        />
        <FormInput
          control={form.control}
          name="amount"
          label={t('Amount (₹)')}
          inputMode="decimal"
          description={chosen ? t('Up to {amount}', { amount: formatINR(chosen.max) }) : undefined}
        />
        <FormInput control={form.control} name="date" label={t('Date')} type="date" min={minDate} max={todayInIndia()} />
      </FieldGroup>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {t('Set credit')}
        </Button>
      </DialogFooter>
    </form>
  )
}
