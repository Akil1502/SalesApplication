import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormTextarea } from '@/components/form/fields'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { ProductPicker, type PickedProduct } from '@/components/ProductPicker'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { formatINR, formatQuantity, todayInIndia } from '@/lib/format'
import { useCreateReceipt } from './api'

const lineSchema = z.object({
  product: z.custom<PickedProduct | null>().refine((p): boolean => p !== null, { error: 'Choose a product.' }),
  quantity: z
    .string()
    .trim()
    .regex(/^\d+(\.\d{1,3})?$/, { error: 'Up to 3 decimals.' })
    .refine((v) => Number(v) > 0, { error: 'More than zero.' }),
  unitCost: z.string().trim().regex(/^(\d+(\.\d{1,2})?)?$/, { error: 'Up to 2 decimals.' }),
})

const schema = z
  .object({
    receiptDate: z.string().min(1, { error: 'Choose a date.' }),
    supplierName: z.string().trim().max(150),
    supplierReference: z.string().trim().max(50),
    notes: z.string().trim().max(1000),
    lines: z.array(lineSchema).min(1, { error: 'Add at least one product.' }),
  })
  .superRefine((v, ctx) => {
    const seen = new Set<number>()
    v.lines.forEach((line, i) => {
      if (!line.product) return
      if (seen.has(line.product.id)) {
        ctx.addIssue({ code: 'custom', path: ['lines', i, 'product'], message: 'Already on this receipt.' })
      }
      seen.add(line.product.id)
    })
  })

type Values = z.infer<typeof schema>
const emptyLine = { product: null, quantity: '', unitCost: '' }

export function ReceiptFormPage() {
  const navigate = useNavigate()
  const create = useCreateReceipt()
  const [createdId, setCreatedId] = useState<number | null>(null)
  useEffect(() => {
    if (createdId) navigate(`/stock/receipts/${createdId}`)
  }, [createdId, navigate])

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { receiptDate: todayInIndia(), supplierName: '', supplierReference: '', notes: '', lines: [emptyLine] },
  })
  const { control, formState } = form
  const lines = useFieldArray({ control, name: 'lines' })
  const watchedLines = useWatch({ control, name: 'lines' })

  const total = watchedLines.reduce((sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitCost) || 0), 0)
  const pickedIds = watchedLines.flatMap((l) => (l.product ? [l.product.id] : []))

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const receipt = await create.mutateAsync({
        receiptDate: values.receiptDate,
        supplierName: values.supplierName || null,
        supplierReference: values.supplierReference || null,
        notes: values.notes || null,
        lines: values.lines.map((l) => ({
          productId: l.product!.id,
          quantity: Number(l.quantity),
          unitCost: l.unitCost === '' ? null : Number(l.unitCost),
        })),
      })
      form.reset(values)
      toast.success(`${receipt.number} recorded`, { description: `${receipt.lines.length} product(s) added to stock.` })
      setCreatedId(receipt.id)
    } catch (error) {
      // Server errors arrive keyed like "lines[1].quantity"; map them onto the matching row.
      if (error instanceof ApiRequestError && error.body.errors) {
        for (const [key, messages] of Object.entries(error.body.errors)) {
          const path = key.replace(/\[(\d+)\]/g, '.$1').replace(/\.productId$/, '.product') as Parameters<typeof form.setError>[0]
          form.setError(path, { type: 'server', message: messages[0] })
        }
      } else {
        toast.error(errorMessage(error))
      }
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-5xl flex-col gap-6">
      <UnsavedChangesGuard when={formState.isDirty && createdId === null} />
      <PageHeader
        title="Receive stock"
        description="Record a delivery. Quantities are added to stock as soon as you save."
        actions={
          <Button variant="ghost" asChild>
            <Link to="/stock?tab=receipts">
              <ArrowLeft />
              Back to stock
            </Link>
          </Button>
        }
      />

      <Card>
        <CardHeader>
          <CardTitle>Delivery details</CardTitle>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-3">
            <FormInput control={control} name="receiptDate" label="Received on" type="date" max={todayInIndia()} />
            <FormInput control={control} name="supplierName" label="Supplier" placeholder="Optional" />
            <FormInput control={control} name="supplierReference" label="Supplier invoice / challan no." placeholder="Optional" />
            <FormTextarea control={control} name="notes" label="Notes" rows={2} className="sm:col-span-3" />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Products received</CardTitle>
          <CardDescription>Cost per unit is optional; it's used to value stock and is visible to managers only.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead className="min-w-72">Product</TableHead>
                  <TableHead className="w-40">Quantity</TableHead>
                  <TableHead className="w-40">Cost per unit (₹)</TableHead>
                  <TableHead className="w-36 text-right">Line total</TableHead>
                  <TableHead className="w-12" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {lines.fields.map((row, index) => {
                  const line = watchedLines[index]
                  const lineTotal = (Number(line?.quantity) || 0) * (Number(line?.unitCost) || 0)
                  return (
                    <TableRow key={row.id} className="align-top hover:bg-transparent">
                      <TableCell>
                        <Controller
                          control={control}
                          name={`lines.${index}.product`}
                          render={({ field, fieldState }) => (
                            <Field data-invalid={fieldState.invalid}>
                              <ProductPicker value={field.value} onChange={field.onChange} excludeIds={pickedIds} invalid={fieldState.invalid} />
                              {field.value && (
                                <span className="text-xs text-muted-foreground">
                                  On hand: {formatQuantity(field.value.quantityOnHand, field.value.unit)}
                                </span>
                              )}
                              <FieldError errors={[fieldState.error]} />
                            </Field>
                          )}
                        />
                      </TableCell>
                      <TableCell>
                        <Controller
                          control={control}
                          name={`lines.${index}.quantity`}
                          render={({ field, fieldState }) => (
                            <Field data-invalid={fieldState.invalid}>
                              <div className="flex items-center gap-2">
                                <Input {...field} inputMode="decimal" aria-label={`Quantity, line ${index + 1}`} aria-invalid={fieldState.invalid} />
                                <span className="text-xs text-muted-foreground">{line?.product?.unit}</span>
                              </div>
                              <FieldError errors={[fieldState.error]} />
                            </Field>
                          )}
                        />
                      </TableCell>
                      <TableCell>
                        <Controller
                          control={control}
                          name={`lines.${index}.unitCost`}
                          render={({ field, fieldState }) => (
                            <Field data-invalid={fieldState.invalid}>
                              <Input {...field} inputMode="decimal" placeholder="Optional" aria-label={`Cost per unit, line ${index + 1}`} aria-invalid={fieldState.invalid} />
                              <FieldError errors={[fieldState.error]} />
                            </Field>
                          )}
                        />
                      </TableCell>
                      <TableCell className="pt-4 text-right tabular-nums">{lineTotal ? formatINR(lineTotal) : '—'}</TableCell>
                      <TableCell>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Remove line ${index + 1}`}
                          disabled={lines.fields.length === 1}
                          onClick={() => lines.remove(index)}
                        >
                          <Trash2 />
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
              <TableFooter>
                <TableRow>
                  <TableCell colSpan={3} className="text-right">
                    Total cost
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatINR(total)}</TableCell>
                  <TableCell />
                </TableRow>
              </TableFooter>
            </Table>
          </div>
          <FieldError errors={[formState.errors.lines?.root ?? formState.errors.lines]} />
          <div>
            <Button type="button" variant="outline" size="sm" onClick={() => lines.append(emptyLine)} disabled={lines.fields.length >= 200}>
              <Plus />
              Add product
            </Button>
          </div>
        </CardContent>
      </Card>

      <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        <Button type="button" variant="outline" onClick={() => navigate('/stock?tab=receipts')}>
          Cancel
        </Button>
        <Button type="submit" disabled={formState.isSubmitting}>
          {formState.isSubmitting && <Spinner />}
          Save receipt
        </Button>
      </div>
    </form>
  )
}
