import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft } from 'lucide-react'
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
import { Roles, useAuth } from '@/features/auth/auth-context'
import { ApiRequestError, applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { useCategories, useHsnCodes } from '@/lib/lookups'
import { productUnits, useProduct, useSaveProduct } from './api'
import { emptyProductForm, productFieldPaths, productFormSchema, toFormValues, toSaveRequest, type ProductFormValues } from './product-form'

export function ProductFormPage() {
  const { id: idParam } = useParams()
  const id = idParam ? Number(idParam) : undefined
  const isNew = id === undefined
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const canEdit = hasRole(Roles.Admin, Roles.SalesManager)
  const readOnly = !canEdit

  const product = useProduct(id)
  // Include inactive entries so an existing product's current category/HSN still shows.
  const categories = useCategories(true)
  const hsnCodes = useHsnCodes(true)
  const save = useSaveProduct()
  const [conflict, setConflict] = useState(false)
  const [saved, setSaved] = useState(false)
  useEffect(() => {
    if (saved) navigate('/products')
  }, [saved, navigate])

  const form = useForm<ProductFormValues>({ resolver: zodResolver(productFormSchema), defaultValues: emptyProductForm })
  const { control, formState, reset } = form

  useEffect(() => {
    if (product.data) reset(toFormValues(product.data))
  }, [product.data, reset])

  const [listPrice, costPrice, hsnCodeId] = useWatch({ control, name: ['listPrice', 'costPrice', 'hsnCodeId'] })
  const selectedHsn = hsnCodes.data?.find((h) => h.id.toString() === hsnCodeId)
  const margin =
    listPrice && costPrice && Number(listPrice) > 0 ? ((Number(listPrice) - Number(costPrice)) / Number(listPrice)) * 100 : null

  const usable = <T extends { id: number; isActive: boolean }>(items: T[] | undefined, currentId: number | undefined) =>
    (items ?? []).filter((item) => item.isActive || item.id === currentId)

  const categoryOptions: SelectOption[] = usable(categories.data, product.data?.categoryId).map((c) => ({
    value: c.id.toString(),
    label: c.isActive ? c.name : `${c.name} (inactive)`,
  }))
  const hsnOptions: SelectOption[] = usable(hsnCodes.data, product.data?.hsnCodeId).map((h) => ({
    value: h.id.toString(),
    label: `${h.code} · ${formatPercent(h.gstRate)} · ${h.description}`,
  }))

  const onSubmit = form.handleSubmit(async (values) => {
    setConflict(false)
    try {
      const result = await save.mutateAsync({ id, body: toSaveRequest(values, product.data?.rowVersion) })
      reset(toFormValues(result))
      toast.success(isNew ? `Product ${result.sku} created` : 'Product saved')
      setSaved(true)
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) setConflict(true)
      else if (!applyFieldErrors(error, form.setError, productFieldPaths)) toast.error(errorMessage(error))
    }
  })

  if (!isNew && product.isPending) return <Skeleton className="h-96 w-full max-w-4xl" />
  if (!isNew && product.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(product.error)}</AlertDescription>
      </Alert>
    )
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-4xl flex-col gap-6">
      <UnsavedChangesGuard when={formState.isDirty && !saved} />

      <PageHeader
        title={isNew ? 'New product' : (product.data?.name ?? 'Product')}
        description={readOnly ? 'Only managers can edit products.' : 'Prices exclude GST.'}
        actions={
          <Button variant="ghost" asChild>
            <Link to="/products">
              <ArrowLeft />
              Back to products
            </Link>
          </Button>
        }
      />

      {conflict && (
        <Alert variant="destructive">
          <AlertDescription className="flex flex-wrap items-center justify-between gap-2">
            Someone else saved this product after you opened it. Reload to see their changes.
            <Button type="button" size="sm" variant="outline" onClick={() => { setConflict(false); void product.refetch() }}>
              Reload
            </Button>
          </AlertDescription>
        </Alert>
      )}

      <fieldset disabled={readOnly} className="contents">
        <Card>
          <CardHeader>
            <CardTitle>Product</CardTitle>
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-2">
              <FormInput control={control} name="sku" label="SKU" className="font-mono uppercase" maxLength={40} disabled={readOnly} />
              <FormSelect control={control} name="unit" label="Unit" options={productUnits} disabled={readOnly} />
              <FormInput control={control} name="name" label="Name" className="sm:col-span-2" disabled={readOnly} />
              <FormTextarea control={control} name="description" label="Description" className="sm:col-span-2" disabled={readOnly} />
              <FormSelect control={control} name="categoryId" label="Category" options={categoryOptions} placeholder="Choose a category" disabled={readOnly} />
              <FormSelect
                control={control}
                name="hsnCodeId"
                label="HSN code"
                options={hsnOptions}
                placeholder="Choose an HSN code"
                disabled={readOnly}
                description={selectedHsn ? `GST ${formatPercent(selectedHsn.gstRate)} on invoices` : undefined}
              />
            </FieldGroup>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Pricing</CardTitle>
            <CardDescription>Per unit, excluding GST.{canEdit && ' Cost price is visible to managers only.'}</CardDescription>
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-3">
              <FormInput control={control} name="listPrice" label="List price (₹)" inputMode="decimal" disabled={readOnly} />
              {canEdit && <FormInput control={control} name="costPrice" label="Cost price (₹)" inputMode="decimal" placeholder="Optional" />}
              {canEdit && (
                <div className="space-y-2 text-sm">
                  <p className="font-medium">Gross margin</p>
                  <p className="text-muted-foreground tabular-nums">
                    {margin === null ? '—' : `${formatPercent(Math.round(margin * 10) / 10)} (${formatINR(Number(listPrice) - Number(costPrice))} per unit)`}
                  </p>
                </div>
              )}
            </FieldGroup>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Stock planning</CardTitle>
            <CardDescription>
              {product.data ? (
                <>
                  In stock: {formatQuantity(product.data.quantityOnHand, product.data.unit)}. Stock changes only through receipts,
                  adjustments and invoices.{' '}
                  <Link
                    className="underline underline-offset-4"
                    to={`/stock?tab=movements&productId=${product.data.id}&product=${encodeURIComponent(`${product.data.sku} · ${product.data.name}`)}`}
                  >
                    View stock ledger
                  </Link>
                </>
              ) : (
                'After saving, add opening stock from Stock → Adjust stock (reason: opening balance) or record a goods receipt.'
              )}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <FieldGroup className="grid gap-4 sm:grid-cols-3">
              <FormInput control={control} name="reorderLevel" label="Reorder level" inputMode="decimal" description="Flag as low stock at or below this." disabled={readOnly} />
              <FormInput control={control} name="reorderQuantity" label="Reorder quantity" inputMode="decimal" disabled={readOnly} />
              <FormInput control={control} name="leadTimeDays" label="Lead time (days)" inputMode="numeric" disabled={readOnly} />
            </FieldGroup>
            <FormSwitch control={control} name="isActive" label="Active" description="Inactive products can't be added to new orders." disabled={readOnly} />
          </CardContent>
        </Card>
      </fieldset>

      {canEdit && (
        <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
          <Button type="button" variant="outline" onClick={() => navigate('/products')}>
            Cancel
          </Button>
          <Button type="submit" disabled={formState.isSubmitting}>
            {formState.isSubmitting && <Spinner />}
            {isNew ? 'Create product' : 'Save changes'}
          </Button>
        </div>
      )}
    </form>
  )
}
