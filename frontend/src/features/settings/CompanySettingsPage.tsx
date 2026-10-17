import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSelect, FormTextarea } from '@/components/form/fields'
import {
  moneyString,
  optionalEmail,
  optionalPhone,
  optionalText,
  pincode,
  requiredGstin,
  requiredText,
  stateCode,
  toNullIfBlank,
  toNumberOrNull,
} from '@/components/form/schemas'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { brandingKey } from '@/features/branding/api'
import { api, type Schemas } from '@/lib/api/client'
import { ApiRequestError, applyFieldErrors, errorMessage, unwrap } from '@/lib/api/errors'
import { gstinStateCode, validateGstin } from '@/lib/gstin'
import { useStates } from '@/lib/lookups'
import { BrandSettingsCard } from './BrandSettingsCard'

type CompanySettings = Schemas['CompanySettingsDto']

// The inputs only *display* upper case (CSS), so normalise the value before checking it.
// At most 4 letters: GST caps invoice numbers at 16 characters (PREF/26-27/00001).
const prefix = z.string().trim().toUpperCase().regex(/^[A-Z]{1,4}$/, { error: 'Use 1 to 4 letters.' })

const schema = z
  .object({
    legalName: requiredText('Legal name', 150),
    tradeName: optionalText(150),
    gstin: requiredGstin,
    stateCode,
    addressLine1: requiredText('Address line 1', 120),
    addressLine2: optionalText(120),
    city: requiredText('City', 60),
    pincode,
    phone: optionalPhone,
    email: optionalEmail,
    bankName: optionalText(100),
    bankAccountNumber: z.string().trim().regex(/^([0-9]{9,18})?$/, { error: 'Account number must be 9 to 18 digits.' }),
    bankIfsc: z.string().trim().toUpperCase().regex(/^([A-Z]{4}0[A-Z0-9]{6})?$/, { error: 'IFSC must be 11 characters, like SBIN0001234.' }),
    bankBranch: optionalText(100),
    invoicePrefix: prefix,
    quotationPrefix: prefix,
    invoiceTerms: optionalText(2000),
    purchaseOrderTerms: optionalText(2000),
    purchaseApprovalLimit: moneyString({ optional: true, max: 999_999_999 }),
    overReceiptTolerancePercent: z.string().trim().regex(/^\d{1,2}(\.\d{1,2})?$/, { error: 'Use 0 to 50, up to 2 decimals.' }).refine((v) => Number(v) <= 50, { error: 'At most 50%.' }),
    billPriceTolerancePercent: z.string().trim().regex(/^\d{1,2}(\.\d{1,2})?$/, { error: 'Use 0 to 50, up to 2 decimals.' }).refine((v) => Number(v) <= 50, { error: 'At most 50%.' }),
    brandColour: z.string().trim().regex(/^(#[0-9a-fA-F]{6})?$/, { error: 'Use a colour like #3655CD, or pick one.' }),
  })
  .refine((v) => validateGstin(v.gstin) !== null || !v.stateCode || gstinStateCode(v.gstin) === v.stateCode, {
    path: ['gstin'],
    error: "The GSTIN's state code doesn't match the company's state.",
  })

type Values = z.infer<typeof schema>
const fields = Object.keys(schema.shape) as (keyof Values)[]

function toValues(s: CompanySettings): Values {
  return {
    legalName: s.legalName,
    tradeName: s.tradeName ?? '',
    gstin: s.gstin,
    stateCode: s.stateCode,
    addressLine1: s.addressLine1,
    addressLine2: s.addressLine2 ?? '',
    city: s.city,
    pincode: s.pincode,
    phone: s.phone ?? '',
    email: s.email ?? '',
    bankName: s.bankName ?? '',
    bankAccountNumber: s.bankAccountNumber ?? '',
    bankIfsc: s.bankIfsc ?? '',
    bankBranch: s.bankBranch ?? '',
    invoicePrefix: s.invoicePrefix,
    quotationPrefix: s.quotationPrefix,
    invoiceTerms: s.invoiceTerms ?? '',
    purchaseOrderTerms: s.purchaseOrderTerms ?? '',
    purchaseApprovalLimit: s.purchaseApprovalLimit?.toString() ?? '',
    overReceiptTolerancePercent: s.overReceiptTolerancePercent.toString(),
    billPriceTolerancePercent: s.billPriceTolerancePercent.toString(),
    brandColour: s.brandColour ?? '',
  }
}

const settingsKey = ['settings', 'company'] as const

/** Financial year label for the example invoice number, e.g. "26-27" (April to March). */
function currentFinancialYear(now = new Date()): string {
  const startYear = now.getMonth() >= 3 ? now.getFullYear() : now.getFullYear() - 1
  return `${String(startYear % 100).padStart(2, '0')}-${String((startYear + 1) % 100).padStart(2, '0')}`
}

export function CompanySettingsPage() {
  const queryClient = useQueryClient()
  const states = useStates()
  const settings = useQuery({ queryKey: settingsKey, queryFn: () => unwrap(api.GET('/api/settings/company')) })
  const save = useMutation({
    mutationFn: (body: Schemas['CompanySettingsSaveRequest']) => unwrap(api.PUT('/api/settings/company', { body })),
    onSuccess: (data) => {
      queryClient.setQueryData(settingsKey, data)
      void queryClient.invalidateQueries({ queryKey: brandingKey })
    },
  })
  const [conflict, setConflict] = useState(false)

  const form = useForm<Values>({ resolver: zodResolver(schema) })
  const { control, reset, formState } = form

  useEffect(() => {
    if (settings.data) reset(toValues(settings.data))
  }, [settings.data, reset])

  const invoicePrefix = useWatch({ control, name: 'invoicePrefix' })

  const onSubmit = form.handleSubmit(async (values) => {
    setConflict(false)
    try {
      await save.mutateAsync({
        ...values,
        gstin: values.gstin.replace(/\s+/g, '').toUpperCase(),
        tradeName: toNullIfBlank(values.tradeName),
        addressLine2: toNullIfBlank(values.addressLine2),
        phone: toNullIfBlank(values.phone),
        email: toNullIfBlank(values.email),
        bankName: toNullIfBlank(values.bankName),
        bankAccountNumber: toNullIfBlank(values.bankAccountNumber),
        bankIfsc: toNullIfBlank(values.bankIfsc),
        bankBranch: toNullIfBlank(values.bankBranch),
        invoiceTerms: toNullIfBlank(values.invoiceTerms),
        purchaseOrderTerms: toNullIfBlank(values.purchaseOrderTerms),
        purchaseApprovalLimit: toNumberOrNull(values.purchaseApprovalLimit),
        overReceiptTolerancePercent: Number(values.overReceiptTolerancePercent),
        billPriceTolerancePercent: Number(values.billPriceTolerancePercent),
        brandColour: toNullIfBlank(values.brandColour)?.toLowerCase() ?? null,
        rowVersion: settings.data!.rowVersion,
      })
      toast.success('Company settings saved')
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) setConflict(true)
      else if (!applyFieldErrors(error, form.setError, fields)) toast.error(errorMessage(error))
    }
  })

  if (settings.isPending) return <Skeleton className="h-96 w-full max-w-4xl" />
  if (settings.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(settings.error)}</AlertDescription>
      </Alert>
    )
  }

  const stateOptions = (states.data ?? []).map((s) => ({ value: s.code, label: `${s.name} (${s.code})` }))

  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-4xl flex-col gap-6">
      <UnsavedChangesGuard when={formState.isDirty} />
      <PageHeader title="Company settings" description="Printed on every quotation and invoice. The state decides CGST + SGST versus IGST." />

      {conflict && (
        <Alert variant="destructive">
          <AlertDescription className="flex flex-wrap items-center justify-between gap-2">
            Another admin saved these settings after you opened them. Reload to see their changes.
            <Button type="button" size="sm" variant="outline" onClick={() => { setConflict(false); void settings.refetch() }}>
              Reload
            </Button>
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Business</CardTitle>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <FormInput control={control} name="legalName" label="Legal name" />
            <FormInput control={control} name="tradeName" label="Trade name" />
            <FormInput control={control} name="gstin" label="GSTIN" className="font-mono uppercase" maxLength={15} />
            <FormSelect control={control} name="stateCode" label="State" options={stateOptions} />
            <FormInput control={control} name="phone" label="Phone" type="tel" />
            <FormInput control={control} name="email" label="Email" type="email" />
          </FieldGroup>
        </CardContent>
      </Card>

      <BrandSettingsCard control={control} name="brandColour" />

      <Card>
        <CardHeader>
          <CardTitle>Registered address</CardTitle>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <FormInput control={control} name="addressLine1" label="Address line 1" className="sm:col-span-2" />
            <FormInput control={control} name="addressLine2" label="Address line 2" className="sm:col-span-2" />
            <FormInput control={control} name="city" label="City" />
            <FormInput control={control} name="pincode" label="PIN code" inputMode="numeric" maxLength={6} />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Bank details</CardTitle>
          <CardDescription>Printed on invoices so customers know where to pay.</CardDescription>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <FormInput control={control} name="bankName" label="Bank" />
            <FormInput control={control} name="bankBranch" label="Branch" />
            <FormInput control={control} name="bankAccountNumber" label="Account number" inputMode="numeric" />
            <FormInput control={control} name="bankIfsc" label="IFSC" className="font-mono uppercase" maxLength={11} />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Documents</CardTitle>
          <CardDescription>
            Invoice numbers restart each financial year (April to March), e.g.{' '}
            <span className="font-mono">{invoicePrefix || 'INV'}/{currentFinancialYear()}/00001</span>. GST allows at most 16 characters.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <FormInput control={control} name="invoicePrefix" label="Invoice prefix" className="font-mono uppercase" maxLength={4} />
            <FormInput control={control} name="quotationPrefix" label="Quotation prefix" className="font-mono uppercase" maxLength={4} />
            <FormTextarea control={control} name="invoiceTerms" label="Terms printed on invoices" rows={4} className="sm:col-span-2" />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Purchasing</CardTitle>
          <CardDescription>Approval for large purchase orders, and how strictly deliveries and supplier bills are checked against the order.</CardDescription>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-3">
            <FormInput
              control={control}
              name="purchaseApprovalLimit"
              label="Approval needed above (₹)"
              inputMode="decimal"
              placeholder="No approvals"
              description="Total with GST. Leave blank to let any order go straight out."
            />
            <FormInput
              control={control}
              name="overReceiptTolerancePercent"
              label="Extra accepted on delivery (%)"
              inputMode="decimal"
              description="Yarn and fabric rarely arrive exact."
            />
            <FormInput
              control={control}
              name="billPriceTolerancePercent"
              label="Bill price difference allowed (%)"
              inputMode="decimal"
              description="Beyond this, a bill needs approval before it's paid."
            />
            <FormTextarea control={control} name="purchaseOrderTerms" label="Terms printed on purchase orders" rows={3} className="sm:col-span-3" />
          </FieldGroup>
        </CardContent>
      </Card>

      <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        <Button type="button" variant="outline" disabled={!formState.isDirty} onClick={() => settings.data && reset(toValues(settings.data))}>
          Discard changes
        </Button>
        <Button type="submit" disabled={formState.isSubmitting || !formState.isDirty}>
          {formState.isSubmitting && <Spinner />}
          Save settings
        </Button>
      </div>
    </form>
  )
}
