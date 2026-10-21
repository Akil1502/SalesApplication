import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSwitch } from '@/components/form/fields'
import { optionalText, requiredText, toNullIfBlank } from '@/components/form/schemas'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { msg, t } from '@/lib/i18n'
import { useSaveCategory, useSaveHsnCode, type Category, type HsnCode } from './api'

interface DialogProps<T> {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The record to edit, or null to create a new one. */
  item: T | null
}

const categorySchema = z.object({
  name: requiredText(msg('Name'), 80),
  description: optionalText(300),
  isActive: z.boolean(),
})
type CategoryValues = z.infer<typeof categorySchema>

export function CategoryDialog({ open, onOpenChange, item }: DialogProps<Category>) {
  const save = useSaveCategory()
  const form = useForm<CategoryValues>({ resolver: zodResolver(categorySchema), defaultValues: { name: '', description: '', isActive: true } })

  useEffect(() => {
    if (open) form.reset({ name: item?.name ?? '', description: item?.description ?? '', isActive: item?.isActive ?? true })
  }, [open, item, form])

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await save.mutateAsync({ id: item?.id, body: { ...values, description: toNullIfBlank(values.description) } })
      toast.success(item ? t('Category saved') : t('Category "{name}" added', { name: values.name }))
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['name', 'description'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{item ? t('Edit category') : t('New category')}</DialogTitle>
            <DialogDescription>{t('Categories group products in lists and reports.')}</DialogDescription>
          </DialogHeader>
          <FieldGroup className="py-4">
            <FormInput control={form.control} name="name" label={t('Name')} autoFocus />
            <FormInput control={form.control} name="description" label={t('Description')} />
            <FormSwitch control={form.control} name="isActive" label={t('Active')} />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              {t('Save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const hsnSchema = z.object({
  code: z.string().trim().regex(/^([0-9]{4}|[0-9]{6}|[0-9]{8})$/, { error: msg('HSN code must be 4, 6 or 8 digits.') }),
  description: requiredText(msg('Description'), 300),
  gstRate: z
    .string()
    .trim()
    .refine((v) => /^\d+(\.\d{1,2})?$/.test(v) && Number(v) <= 40, { error: msg('GST rate must be between 0% and 40%.') }),
  isActive: z.boolean(),
})
type HsnValues = z.infer<typeof hsnSchema>

export function HsnCodeDialog({ open, onOpenChange, item }: DialogProps<HsnCode>) {
  const save = useSaveHsnCode()
  const form = useForm<HsnValues>({
    resolver: zodResolver(hsnSchema),
    defaultValues: { code: '', description: '', gstRate: '', isActive: true },
  })

  useEffect(() => {
    if (open) {
      form.reset({
        code: item?.code ?? '',
        description: item?.description ?? '',
        gstRate: item?.gstRate.toString() ?? '',
        isActive: item?.isActive ?? true,
      })
    }
  }, [open, item, form])

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await save.mutateAsync({ id: item?.id, body: { ...values, gstRate: Number(values.gstRate) } })
      toast.success(item ? t('HSN {code} saved', { code: values.code }) : t('HSN {code} added', { code: values.code }))
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['code', 'description', 'gstRate'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{item ? t('Edit HSN {code}', { code: item.code }) : t('New HSN code')}</DialogTitle>
            <DialogDescription>{t('A rate change applies to invoices raised from now on. Invoices already issued keep their rate.')}</DialogDescription>
          </DialogHeader>
          <FieldGroup className="grid gap-4 py-4 sm:grid-cols-2">
            <FormInput control={form.control} name="code" label={t('HSN code')} inputMode="numeric" maxLength={8} autoFocus />
            <FormInput control={form.control} name="gstRate" label={t('GST rate (%)')} inputMode="decimal" description={t('Total of CGST + SGST, or IGST.')} />
            <FormInput control={form.control} name="description" label={t('Description')} className="sm:col-span-2" />
            <FormSwitch control={form.control} name="isActive" label={t('Active')} className="sm:col-span-2" />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              {t('Save')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
