import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useId } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { SegmentedControl } from '@/components/SegmentedControl'
import { FormInput, FormSelect, FormTextarea } from '@/components/form/fields'
import { ProductPicker, type PickedProduct } from '@/components/ProductPicker'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatQuantity, todayInIndia } from '@/lib/format'
import { msg, t } from '@/lib/i18n'
import { adjustmentReasons, useAdjustStock, type AdjustmentMode, type AdjustmentReason } from './api'

const schema = z
  .object({
    product: z.custom<PickedProduct | null>().refine((p): boolean => p !== null, { error: () => t('Choose a product.') }),
    mode: z.enum(['Change', 'SetCount']),
    quantity: z.string().trim().regex(/^-?\d+(\.\d{1,3})?$/, { error: () => t('Enter a quantity with up to 3 decimals.') }),
    reason: z.string().min(1, { error: () => t('Choose a reason.') }),
    movementDate: z.string().min(1, { error: () => t('Choose a date.') }),
    notes: z.string().trim().min(1, { error: () => t('Explain the adjustment.') }).max(500),
  })
  .superRefine((v, ctx) => {
    const n = Number(v.quantity)
    if (v.mode === 'Change' && n === 0) ctx.addIssue({ code: 'custom', path: ['quantity'], message: t('Enter how much to add (+) or remove (−).') })
    if (v.mode === 'SetCount' && n < 0) ctx.addIssue({ code: 'custom', path: ['quantity'], message: t("A counted quantity can't be negative.") })
  })

type Values = z.infer<typeof schema>

interface AdjustStockDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Pre-selects a product, e.g. when opened from a row in the stock list. */
  product?: PickedProduct | null
}

export function AdjustStockDialog({ open, onOpenChange, product = null }: AdjustStockDialogProps) {
  const adjust = useAdjustStock()
  const pickerId = useId()
  const form = useForm<Values>({ resolver: zodResolver(schema) })
  const { control } = form

  useEffect(() => {
    if (open) {
      form.reset({ product, mode: 'Change', quantity: '', reason: 'StockCount', movementDate: todayInIndia(), notes: '' })
    }
  }, [open, product, form])

  const [picked, mode, quantity] = useWatch({ control, name: ['product', 'mode', 'quantity'] })
  const n = Number(quantity)
  const valid = quantity !== '' && !Number.isNaN(n)
  const resulting = picked && valid ? (mode === 'SetCount' ? n : picked.quantityOnHand + n) : null

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const movement = await adjust.mutateAsync({
        productId: values.product!.id,
        mode: values.mode as AdjustmentMode,
        quantity: Number(values.quantity),
        reason: values.reason as AdjustmentReason,
        movementDate: values.movementDate,
        notes: values.notes,
      })
      toast.success(t('{product}: stock is now {quantity}', { product: movement.productName, quantity: formatQuantity(movement.balanceAfter, movement.unit) }))
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['quantity', 'reason', 'movementDate', 'notes'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{t('Adjust stock')}</DialogTitle>
            <DialogDescription>{t('Corrections are recorded in the stock ledger with your name and the reason.')}</DialogDescription>
          </DialogHeader>

          <FieldGroup className="py-4">
            <Controller
              control={control}
              name="product"
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor={pickerId}>{t('Product')}</FieldLabel>
                  <ProductPicker id={pickerId} value={field.value ?? null} onChange={field.onChange} invalid={fieldState.invalid} />
                  {field.value && (
                    <p className="text-sm text-muted-foreground">{t('On hand now: {quantity}', { quantity: formatQuantity(field.value.quantityOnHand, field.value.unit) })}</p>
                  )}
                  <FieldError errors={[fieldState.error]} />
                </Field>
              )}
            />

            <Controller
              control={control}
              name="mode"
              render={({ field }) => (
                <SegmentedControl
                  label={msg('How to adjust')}
                  fullWidth
                  value={field.value}
                  onValueChange={field.onChange}
                  options={[
                    { value: 'Change', label: msg('Add or remove') },
                    { value: 'SetCount', label: msg('Set counted quantity') },
                  ]}
                />
              )}
            />

            <div className="grid gap-4 sm:grid-cols-2">
              <FormInput
                control={control}
                name="quantity"
                label={mode === 'SetCount' ? t('Counted quantity') : t('Change (+ adds, − removes)')}
                inputMode="decimal"
                placeholder={mode === 'SetCount' ? t('e.g. 480') : t('e.g. -12.5')}
                description={
                  resulting === null
                    ? undefined
                    : resulting < 0
                      ? t("That's more than the {quantity} in stock; stock can't go below zero.", { quantity: formatQuantity(picked!.quantityOnHand, picked!.unit) })
                      : t('Stock after: {quantity}', { quantity: formatQuantity(resulting, picked!.unit) })
                }
              />
              <FormInput control={control} name="movementDate" label={t('Date')} type="date" max={todayInIndia()} />
            </div>

            <FormSelect control={control} name="reason" label={t('Reason')} options={adjustmentReasons} />
            <FormTextarea control={control} name="notes" label={t('Notes')} description={t("Required. For example: 'Monthly count, bay 3' or 'Water damage in godown'.")} />
          </FieldGroup>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              {t('Post adjustment')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
