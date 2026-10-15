import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm, useWatch, type Control } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { FormCheckbox, FormInput, FormSelect, FormSwitch, FormTextarea, type SelectOption } from '@/components/form/fields'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { ApiRequestError, applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { gstinPan, gstinStateCode, validateGstin } from '@/lib/gstin'
import { useSalesReps, useStates } from '@/lib/lookups'
import { useCustomer, useSaveCustomer } from './api'
import {
  customerFieldPaths,
  customerFormSchema,
  emptyCustomerForm,
  toFormValues,
  toSaveRequest,
  type CustomerFormValues,
} from './customer-form'

function AddressFields({
  control,
  prefix,
  stateOptions,
}: {
  control: Control<CustomerFormValues>
  prefix: 'billingAddress' | 'shippingAddress'
  stateOptions: SelectOption[]
}) {
  return (
    <FieldGroup className="grid gap-4 sm:grid-cols-2">
      <FormInput control={control} name={`${prefix}.line1`} label="Address line 1" className="sm:col-span-2" />
      <FormInput control={control} name={`${prefix}.line2`} label="Address line 2" className="sm:col-span-2" />
      <FormInput control={control} name={`${prefix}.city`} label="City" />
      <FormInput control={control} name={`${prefix}.pincode`} label="PIN code" inputMode="numeric" maxLength={6} />
      <FormSelect control={control} name={`${prefix}.stateCode`} label="State" options={stateOptions} placeholder="Choose a state" className="sm:col-span-2" />
    </FieldGroup>
  )
}

export function CustomerFormPage() {
  const { id: idParam } = useParams()
  const id = idParam ? Number(idParam) : undefined
  const isNew = id === undefined
  const navigate = useNavigate()
  const { can } = useAuth()
  const canEdit = can(Permissions.CustomersEdit)
  const canEditTerms = canEdit && can(Permissions.CreditTermsEdit)

  const customer = useCustomer(id)
  const states = useStates()
  const reps = useSalesReps(canEditTerms)
  const save = useSaveCustomer()
  const [conflict, setConflict] = useState(false)
  // Navigate after the post-save render, so the unsaved-changes guard sees a clean form.
  const [saved, setSaved] = useState(false)
  useEffect(() => {
    if (saved) navigate('/customers')
  }, [saved, navigate])

  const form = useForm<CustomerFormValues>({
    resolver: zodResolver(customerFormSchema),
    defaultValues: emptyCustomerForm,
  })
  const { control, formState, reset, setValue, getValues } = form

  // Load the record into the form once it arrives (and again after "reload" on a conflict).
  useEffect(() => {
    if (customer.data) reset(toFormValues(customer.data))
  }, [customer.data, reset])

  const gstin = useWatch({ control, name: 'gstin' })
  const sameAsBilling = useWatch({ control, name: 'shippingSameAsBilling' })
  const gstinIsValid = !!gstin && validateGstin(gstin) === null

  // A valid GSTIN tells us the registered state: fill it in if the user hasn't picked one yet.
  useEffect(() => {
    if (gstinIsValid && !getValues('billingAddress.stateCode')) {
      setValue('billingAddress.stateCode', gstinStateCode(gstin), { shouldDirty: true })
    }
  }, [gstin, gstinIsValid, getValues, setValue])

  const stateOptions: SelectOption[] = (states.data ?? []).map((s) => ({ value: s.code, label: `${s.name} (${s.code})` }))
  const repOptions: SelectOption[] = (reps.data ?? []).map((r) => ({ value: r.id, label: r.fullName }))
  // Keep the current rep visible even if they're no longer in the active list.
  if (customer.data?.assignedRepId && !repOptions.some((o) => o.value === customer.data?.assignedRepId)) {
    repOptions.push({ value: customer.data.assignedRepId, label: customer.data.assignedRepName ?? 'Current rep' })
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setConflict(false)
    try {
      const result = await save.mutateAsync({ id, body: toSaveRequest(values, customer.data?.rowVersion) })
      reset(toFormValues(result))
      toast.success(isNew ? `Customer ${result.code} created` : 'Customer saved')
      setSaved(true)
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        setConflict(true)
      } else if (!applyFieldErrors(error, form.setError, customerFieldPaths)) {
        toast.error(errorMessage(error))
      }
    }
  })

  if (!isNew && customer.isPending) {
    return <Skeleton className="h-96 w-full max-w-4xl" />
  }

  if (!isNew && customer.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(customer.error)}</AlertDescription>
      </Alert>
    )
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-4xl flex-col gap-6">
      <fieldset disabled={!canEdit} className="contents">
      <UnsavedChangesGuard when={formState.isDirty && !saved} />

      <PageHeader
        title={isNew ? 'New customer' : (customer.data?.name ?? 'Customer')}
        description={isNew ? 'Leave the code blank to generate one.' : `Customer code ${customer.data?.code}`}
        actions={
          <Button variant="ghost" asChild>
            <Link to="/customers">
              <ArrowLeft />
              Back to customers
            </Link>
          </Button>
        }
      />

      {conflict && (
        <Alert variant="destructive">
          <AlertDescription className="flex flex-wrap items-center justify-between gap-2">
            Someone else saved this customer after you opened it. Reload to see their changes (your edits will be discarded).
            <Button type="button" size="sm" variant="outline" onClick={() => { setConflict(false); void customer.refetch() }}>
              Reload
            </Button>
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Business details</CardTitle>
          <CardDescription>Use the legal name exactly as registered for GST.</CardDescription>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            {isNew && <FormInput control={control} name="code" label="Customer code" placeholder="Auto" maxLength={20} />}
            <FormInput control={control} name="name" label="Legal name" className={isNew ? '' : 'sm:col-span-2'} />
            <FormInput control={control} name="tradeName" label="Trade name" />
            <FormInput
              control={control}
              name="gstin"
              label="GSTIN"
              placeholder="Leave blank if unregistered"
              maxLength={15}
              className="font-mono uppercase"
              description={gstinIsValid ? `PAN ${gstinPan(gstin)}` : 'Leave blank for unregistered (B2C) customers.'}
            />
            <FormInput control={control} name="contactPerson" label="Contact person" />
            <FormInput control={control} name="phone" label="Phone" type="tel" placeholder="+91 98400 12345" />
            <FormInput control={control} name="email" label="Email" type="email" className="sm:col-span-2" />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Billing address</CardTitle>
          <CardDescription>The GST-registered address. Its state decides intra-state (CGST + SGST) or inter-state (IGST) tax.</CardDescription>
        </CardHeader>
        <CardContent>
          <AddressFields control={control} prefix="billingAddress" stateOptions={stateOptions} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Shipping address</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <FormCheckbox control={control} name="shippingSameAsBilling" label="Same as billing address" />
          {!sameAsBilling && <AddressFields control={control} prefix="shippingAddress" stateOptions={stateOptions} />}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Commercial terms</CardTitle>
          <CardDescription>
            {canEditTerms ? 'Credit limit and payment terms apply to new orders.' : 'Changing credit terms and the sales rep needs the "Set credit terms" permission.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-3">
            <FormInput
              control={control}
              name="creditLimit"
              label="Credit limit (₹)"
              inputMode="decimal"
              placeholder="No limit"
              disabled={!canEditTerms}
            />
            <FormInput control={control} name="paymentTermsDays" label="Payment terms (days)" inputMode="numeric" disabled={!canEditTerms} />
            {canEditTerms ? (
              <FormSelect control={control} name="assignedRepId" label="Sales rep" options={repOptions} noneLabel="Unassigned" />
            ) : (
              <div className="space-y-2 text-sm">
                <p className="font-medium">Sales rep</p>
                <p className="text-muted-foreground">{customer.data?.assignedRepName ?? (isNew ? 'You' : 'Unassigned')}</p>
              </div>
            )}
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Notes</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <FormTextarea control={control} name="notes" label="Internal notes" description="Visible to your team only." />
          <FormSwitch control={control} name="isActive" label="Active" description="Inactive customers are hidden from lists and can't be used on new orders." />
        </CardContent>
      </Card>

      </fieldset>

      {canEdit && (
        <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
          <Button type="button" variant="outline" onClick={() => navigate('/customers')}>
            Cancel
          </Button>
          <Button type="submit" disabled={formState.isSubmitting}>
            {formState.isSubmitting && <Spinner />}
            {isNew ? 'Create customer' : 'Save changes'}
          </Button>
        </div>
      )}
    </form>
  )
}
