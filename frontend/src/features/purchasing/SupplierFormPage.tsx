import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, ClipboardList, Plus } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { FormInput, FormSelect, FormSwitch, FormTextarea, type SelectOption } from '@/components/form/fields'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { RecordHistory } from '@/features/audit/RecordHistory'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { ApiRequestError, applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatINR } from '@/lib/format'
import { gstinPan, gstinStateCode, validateGstin } from '@/lib/gstin'
import { useStates } from '@/lib/lookups'
import { useSaveSupplier, useSupplier } from './api'
import { emptySupplierForm, supplierFieldPaths, supplierFormSchema, toFormValues, toSaveRequest, type SupplierFormValues } from './supplier-form'
import { documentLanguageOptions } from '@/features/sales/print/document-language'
import { t } from '@/lib/i18n'

export function SupplierFormPage() {
  const { id: idParam } = useParams()
  const id = idParam ? Number(idParam) : undefined
  const isNew = id === undefined
  const navigate = useNavigate()
  const { can } = useAuth()
  const canEdit = can(Permissions.SuppliersEdit)

  const supplier = useSupplier(id)
  const states = useStates()
  const save = useSaveSupplier()
  const [conflict, setConflict] = useState(false)
  // Navigate after the post-save render, so the unsaved-changes guard sees a clean form.
  const [saved, setSaved] = useState(false)
  useEffect(() => {
    if (saved) navigate('/suppliers')
  }, [saved, navigate])

  const form = useForm<SupplierFormValues>({ resolver: zodResolver(supplierFormSchema), defaultValues: emptySupplierForm })
  const { control, formState, reset, setValue, getValues } = form

  useEffect(() => {
    if (supplier.data) reset(toFormValues(supplier.data))
  }, [supplier.data, reset])

  const gstin = useWatch({ control, name: 'gstin' })
  const gstinIsValid = !!gstin && validateGstin(gstin) === null

  // A valid GSTIN tells us the registered state: fill it in if the user hasn't picked one yet.
  useEffect(() => {
    if (gstinIsValid && !getValues('address.stateCode')) {
      setValue('address.stateCode', gstinStateCode(gstin), { shouldDirty: true })
    }
  }, [gstin, gstinIsValid, getValues, setValue])

  const stateOptions: SelectOption[] = (states.data ?? []).map((s) => ({ value: s.code, label: `${s.name} (${s.code})` }))

  const onSubmit = form.handleSubmit(async (values) => {
    setConflict(false)
    try {
      const result = await save.mutateAsync({ id, body: toSaveRequest(values, supplier.data?.rowVersion) })
      reset(toFormValues(result))
      toast.success(isNew ? t('Supplier {code} created', { code: result.code }) : t('Supplier saved'))
      setSaved(true)
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        setConflict(true)
      } else if (!applyFieldErrors(error, form.setError, supplierFieldPaths)) {
        toast.error(errorMessage(error))
      }
    }
  })

  if (!isNew && supplier.isPending) return <Skeleton className="h-96 w-full max-w-4xl" />
  if (!isNew && supplier.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(supplier.error)}</AlertDescription>
      </Alert>
    )
  }

  const s = supplier.data

  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-4xl flex-col gap-6">
      <fieldset disabled={!canEdit} className="contents">
        <UnsavedChangesGuard when={formState.isDirty && !saved} />

        <PageHeader
          title={isNew ? t('New supplier') : (s?.name ?? t('Supplier'))}
          description={
            isNew
              ? t('Leave the code blank to generate one.')
              : s?.isInterState
                ? t('Supplier code {code} · in another state: their bills carry IGST', { code: s.code })
                : t('Supplier code {code} · in your state: their bills carry CGST + SGST', { code: s?.code ?? '' })
          }
          actions={
            <>
              <Button variant="ghost" asChild>
                <Link to="/suppliers">
                  <ArrowLeft />
                  {t('Back to suppliers')}
                </Link>
              </Button>
              {s && can(Permissions.PurchaseOrdersView) && (
                <Button variant="outline" asChild>
                  <Link to={`/purchase-orders?supplierId=${s.id}&supplier=${encodeURIComponent(s.name)}`}>
                    <ClipboardList />
                    {t('Orders')}
                  </Link>
                </Button>
              )}
              {s?.isActive && can(Permissions.PurchaseOrdersEdit) && (
                <Button asChild>
                  <Link to={`/purchase-orders/new?supplierId=${s.id}`}>
                    <Plus />
                    {t('New order')}
                  </Link>
                </Button>
              )}
            </>
          }
        />

        {s && (s.payable > 0 || s.openOrders > 0) && (
          <div className="grid gap-4 sm:grid-cols-3">
            {[
              { label: t('We owe'), value: formatINR(s.payable, 0) },
              { label: t('Overdue'), value: formatINR(s.overdue, 0), warn: s.overdue > 0 },
              { label: t('Open orders'), value: String(s.openOrders) },
            ].map((card) => (
              <Card key={card.label} className="gap-1 py-4">
                <CardHeader className="px-4">
                  <CardDescription>{card.label}</CardDescription>
                </CardHeader>
                <CardContent className="px-4">
                  <p className={card.warn ? 'text-2xl font-semibold text-destructive tabular-nums' : 'text-2xl font-semibold tabular-nums'}>{card.value}</p>
                </CardContent>
              </Card>
            ))}
          </div>
        )}

        {conflict && (
          <Alert variant="destructive">
            <AlertDescription className="flex flex-wrap items-center justify-between gap-2">
              {t('Someone else saved this supplier after you opened it. Reload to see their changes (your edits will be discarded).')}
              <Button type="button" size="sm" variant="outline" onClick={() => { setConflict(false); void supplier.refetch() }}>
                {t('Reload')}
              </Button>
            </AlertDescription>
          </Alert>
        )}

        <Card>
          <CardHeader>
            <CardTitle>{t('Business details')}</CardTitle>
            <CardDescription>{t('Use the legal name exactly as on their GST registration and invoices.')}</CardDescription>
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-2">
              {isNew && <FormInput control={control} name="code" label={t('Supplier code')} placeholder={t('Auto')} maxLength={20} />}
              <FormInput control={control} name="name" label={t('Legal name')} className={isNew ? '' : 'sm:col-span-2'} />
              <FormInput control={control} name="tradeName" label={t('Trade name')} />
              <FormInput
                control={control}
                name="gstin"
                label="GSTIN"
                placeholder={t('Leave blank if unregistered')}
                maxLength={15}
                className="font-mono uppercase"
                description={gstinIsValid ? `PAN ${gstinPan(gstin)}` : t('Without one, GST on their bills can’t be claimed as input credit.')}
              />
              <FormInput control={control} name="contactPerson" label={t('Contact person')} />
              <FormInput control={control} name="phone" label={t('Phone')} type="tel" placeholder="+91 98400 12345" />
              <FormInput control={control} name="email" label={t('Email')} type="email" className="sm:col-span-2" />
            </FieldGroup>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t('Registered address')}</CardTitle>
            <CardDescription>{t('The address on their GST registration. Its state decides IGST or CGST + SGST on their bills.')}</CardDescription>
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-2">
              <FormInput control={control} name="address.line1" label={t('Address line 1')} className="sm:col-span-2" />
              <FormInput control={control} name="address.line2" label={t('Address line 2')} className="sm:col-span-2" />
              <FormInput control={control} name="address.city" label={t('City')} />
              <FormInput control={control} name="address.pincode" label={t('PIN code')} inputMode="numeric" maxLength={6} />
              <FormSelect control={control} name="address.stateCode" label={t('State')} options={stateOptions} placeholder={t('Choose a state')} className="sm:col-span-2" />
            </FieldGroup>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t('Paying them')}</CardTitle>
            <CardDescription>{t('A bill falls due this many days after its date.')}</CardDescription>
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-2">
              <FormInput control={control} name="paymentTermsDays" label={t('Payment terms (days)')} inputMode="numeric" />
              <FormInput control={control} name="bankName" label={t('Bank')} />
              <FormInput control={control} name="bankAccountNumber" label={t('Account number')} inputMode="numeric" />
              <FormInput control={control} name="bankIfsc" label="IFSC" className="font-mono uppercase" maxLength={11} />
            </FieldGroup>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t('Notes')}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <FormSelect
              control={control}
              name="documentLanguage"
              label={t('Language for documents')}
              description={t('Purchase orders and debit notes for this supplier.')}
              options={documentLanguageOptions()}
              noneLabel={t("Company's default")}
            />
            <FormTextarea control={control} name="notes" label={t('Internal notes')} description={t('Visible to your team only.')} />
            <FormSwitch control={control} name="isActive" label={t('Active')} description={t("Inactive suppliers are hidden from lists and can't be used on new orders.")} />
          </CardContent>
        </Card>
      </fieldset>

      {id !== undefined && <RecordHistory entityType="Supplier" entityId={id} />}

      {canEdit && (
        <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
          <Button type="button" variant="outline" onClick={() => navigate('/suppliers')}>
            {t('Cancel')}
          </Button>
          <Button type="submit" disabled={formState.isSubmitting}>
            {formState.isSubmitting && <Spinner />}
            {isNew ? t('Create supplier') : t('Save changes')}
          </Button>
        </div>
      )}
    </form>
  )
}
