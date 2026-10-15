import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, Ban, CheckCircle2, FileText, Plus, Printer, ReceiptIndianRupee, Send, Trash2, TriangleAlert } from 'lucide-react'
import { useEffect, useId, useMemo, useState } from 'react'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { CustomerPicker } from '@/components/CustomerPicker'
import { FormInput, FormTextarea } from '@/components/form/fields'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { ProductPicker } from '@/components/ProductPicker'
import { ReasonDialog } from '@/components/ReasonDialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { useCustomer } from '@/features/customers/api'
import { api } from '@/lib/api/client'
import { ApiRequestError, errorMessage, unwrap } from '@/lib/api/errors'
import { formatDate, formatINR, formatPercent, formatQuantity, todayInIndia } from '@/lib/format'
import { calculateTotals } from '@/lib/gst'
import { useStates } from '@/lib/lookups'
import { cn } from '@/lib/utils'
import { useCancelOrder, useConfirmOrder, useOrder, useQuoteOrder, useSaveOrder, type Order } from './api'
import { OrderStatusBadge } from './badges'
import { CreateInvoiceDialog } from './CreateInvoiceDialog'
import { emptyLine, emptyOrderForm, orderFormSchema, toFormValues, toSaveRequest, type OrderFormValues } from './order-form'

function TotalsRow({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={cn('flex justify-between gap-8 tabular-nums', strong && 'border-t pt-2 text-base font-semibold')}>
      <span className={strong ? '' : 'text-muted-foreground'}>{label}</span>
      <span>{value}</span>
    </div>
  )
}

/** Turns a server key like "lines[1].productId" into the form path "lines.1.product". */
function toFormPath(key: string): string {
  return key.replace(/\[(\d+)\]/g, '.$1').replace(/\.productId$/, '.product').replace(/^customerId$/, 'customer')
}

export function OrderFormPage() {
  const { id: idParam } = useParams()
  const id = idParam ? Number(idParam) : undefined
  const isNew = id === undefined
  const navigate = useNavigate()
  const { can } = useAuth()
  const canEdit = can(Permissions.OrdersEdit)
  const canOverride = can(Permissions.CreditLimitOverride)
  const customerPickerId = useId()

  const order = useOrder(id)
  const states = useStates()
  const company = useQuery({ queryKey: ['settings', 'company'], queryFn: () => unwrap(api.GET('/api/settings/company')), staleTime: 5 * 60_000 })

  const save = useSaveOrder()
  const quote = useQuoteOrder()
  const confirm = useConfirmOrder()
  const cancel = useCancelOrder()

  const [creditProblem, setCreditProblem] = useState<string | null>(null)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [invoiceOpen, setInvoiceOpen] = useState(false)
  const [createdId, setCreatedId] = useState<number | null>(null)
  useEffect(() => {
    if (createdId) navigate(`/orders/${createdId}`, { replace: true })
  }, [createdId, navigate])

  const form = useForm<OrderFormValues>({ resolver: zodResolver(orderFormSchema), defaultValues: emptyOrderForm() })
  const { control, formState, reset } = form
  const lines = useFieldArray({ control, name: 'lines' })

  useEffect(() => {
    if (order.data) reset(toFormValues(order.data))
  }, [order.data, reset])

  const status = order.data?.status
  const editable = canEdit && (isNew || status === 'Draft' || status === 'Quoted')
  const dirty = formState.isDirty

  // ---- Live GST preview: place of supply comes from the customer's shipping (else billing) state.
  const [customer, watchedLines] = useWatch({ control, name: ['customer', 'lines'] })
  const customerDetail = useCustomer(customer?.id)
  const posCode = customerDetail.data ? (customerDetail.data.shippingAddress?.stateCode ?? customerDetail.data.billingAddress.stateCode) : order.data?.placeOfSupplyStateCode
  const posState = states.data?.find((s) => s.code === posCode)
  const isInterState = !!posCode && !!company.data && posCode !== company.data.stateCode
  const usesUtgst = !isInterState && !!posState?.usesUtgst

  const preview = useMemo(
    () =>
      calculateTotals(
        watchedLines.map((l) => ({
          quantity: Number(l.quantity) || 0,
          unitPrice: Number(l.unitPrice) || 0,
          discountPercent: Number(l.discountPercent) || 0,
          gstRate: l.product?.gstRate ?? 0,
        })),
        isInterState,
      ),
    [watchedLines, isInterState],
  )
  const pickedIds = watchedLines.flatMap((l) => (l.product ? [l.product.id] : []))

  // ---- Actions
  const applyServerErrors = (error: unknown) => {
    if (error instanceof ApiRequestError && error.body.errors) {
      for (const [key, messages] of Object.entries(error.body.errors)) {
        form.setError(toFormPath(key) as Parameters<typeof form.setError>[0], { type: 'server', message: messages[0] })
      }
      return
    }
    toast.error(errorMessage(error))
  }

  const onSave = form.handleSubmit(async (values) => {
    try {
      const saved = await save.mutateAsync({ id, body: toSaveRequest(values, order.data?.rowVersion) })
      reset(toFormValues(saved))
      toast.success(isNew ? `Order ${saved.number} created` : 'Order saved')
      if (isNew) setCreatedId(saved.id)
    } catch (error) {
      applyServerErrors(error)
    }
  })

  const runAction = async (action: () => Promise<Order>, success: string) => {
    try {
      const updated = await action()
      reset(toFormValues(updated))
      toast.success(success)
    } catch (error) {
      if (error instanceof ApiRequestError && error.body.errors?.creditLimit) {
        setCreditProblem(error.body.errors.creditLimit[0] ?? error.body.message)
      } else {
        toast.error(errorMessage(error))
      }
    }
  }

  const doConfirm = (overrideCreditLimit: boolean) =>
    runAction(() => confirm.mutateAsync({ id: id!, rowVersion: order.data!.rowVersion, overrideCreditLimit }), `Order ${order.data!.number} confirmed`)

  if (!isNew && order.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (!isNew && order.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(order.error)}</AlertDescription>
      </Alert>
    )
  }

  const o = order.data
  const busy = save.isPending || quote.isPending || confirm.isPending || cancel.isPending

  return (
    <>
    <form onSubmit={onSave} noValidate className="flex max-w-6xl flex-col gap-6">
      <UnsavedChangesGuard when={dirty && createdId === null} />

      <PageHeader
        title={isNew ? 'New order' : (o?.number ?? 'Order')}
        description={
          o ? (
            <span className="inline-flex flex-wrap items-center gap-2">
              <OrderStatusBadge status={o.status} />
              {o.quotationNumber && <span className="font-mono text-xs">Quotation {o.quotationNumber}</span>}
              {o.invoiceNumber && (
                <Link className="font-mono text-xs underline underline-offset-4" to={`/invoices/${o.invoiceId}`}>
                  Invoice {o.invoiceNumber}
                </Link>
              )}
            </span>
          ) : (
            'Start as a draft. Mark it as quoted to send to the customer, then confirm when they accept.'
          )
        }
        actions={
          <Button variant="ghost" asChild>
            <Link to="/orders">
              <ArrowLeft />
              Back to orders
            </Link>
          </Button>
        }
      />

      {o?.status === 'Cancelled' && (
        <Alert variant="destructive">
          <Ban />
          <AlertDescription>Cancelled {formatDate(o.cancelledAtUtc)}: {o.cancellationReason}</AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Customer & terms</CardTitle>
          {posState && (
            <CardDescription>
              Place of supply: {posState.name} ({posState.code}) —{' '}
              {isInterState ? 'inter-state, IGST applies' : `intra-state, CGST + ${usesUtgst ? 'UTGST' : 'SGST'} apply`}
            </CardDescription>
          )}
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Controller
              control={control}
              name="customer"
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid} className="sm:col-span-2">
                  <FieldLabel htmlFor={customerPickerId}>Customer</FieldLabel>
                  <CustomerPicker id={customerPickerId} value={field.value} onChange={field.onChange} disabled={!editable} invalid={fieldState.invalid} />
                  <FieldError errors={[fieldState.error]} />
                </Field>
              )}
            />
            <FormInput control={control} name="orderDate" label="Order date" type="date" max={todayInIndia()} disabled={!editable} />
            <FormInput control={control} name="validUntil" label="Quote valid until" type="date" disabled={!editable} />
            <FormInput control={control} name="customerReference" label="Customer PO number" placeholder="Optional" disabled={!editable} className="sm:col-span-2" />
            <FormTextarea control={control} name="notes" label="Notes" rows={1} disabled={!editable} className="sm:col-span-2" />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Items</CardTitle>
          <CardDescription>Prices exclude GST. The rate comes from each product's HSN code.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead className="min-w-64">Product</TableHead>
                  <TableHead className="w-32">Quantity</TableHead>
                  <TableHead className="w-32">Price (₹)</TableHead>
                  <TableHead className="w-24">Disc %</TableHead>
                  <TableHead className="w-16 text-right">GST</TableHead>
                  <TableHead className="w-32 text-right">Taxable</TableHead>
                  <TableHead className="w-32 text-right">Total</TableHead>
                  {editable && <TableHead className="w-12" />}
                </TableRow>
              </TableHeader>
              <TableBody>
                {lines.fields.map((row, index) => {
                  const line = watchedLines[index]
                  const result = preview.lines[index]
                  const short = !!line?.product && Number(line.quantity) > line.product.quantityOnHand
                  return (
                    <TableRow key={row.id} className="align-top hover:bg-transparent">
                      <TableCell>
                        <Controller
                          control={control}
                          name={`lines.${index}.product`}
                          render={({ field, fieldState }) => (
                            <Field data-invalid={fieldState.invalid}>
                              <ProductPicker
                                value={field.value}
                                disabled={!editable}
                                excludeIds={pickedIds}
                                invalid={fieldState.invalid}
                                onChange={(product) => {
                                  field.onChange(product)
                                  // A newly picked product starts at its list price.
                                  form.setValue(`lines.${index}.unitPrice`, product.listPrice.toFixed(2), { shouldDirty: true })
                                }}
                              />
                              {line?.product && (
                                <span className={cn('inline-flex items-center gap-1 text-xs', short ? 'text-amber-700 dark:text-amber-400' : 'text-muted-foreground')}>
                                  {short && <TriangleAlert className="size-3" />}
                                  {formatQuantity(line.product.quantityOnHand, line.product.unit)} in stock
                                </span>
                              )}
                              <FieldError errors={[fieldState.error]} />
                            </Field>
                          )}
                        />
                      </TableCell>
                      {(['quantity', 'unitPrice', 'discountPercent'] as const).map((name) => (
                        <TableCell key={name}>
                          <Controller
                            control={control}
                            name={`lines.${index}.${name}`}
                            render={({ field, fieldState }) => (
                              <Field data-invalid={fieldState.invalid}>
                                <Input
                                  {...field}
                                  inputMode="decimal"
                                  disabled={!editable}
                                  placeholder={name === 'discountPercent' ? '0' : undefined}
                                  aria-label={`${name === 'quantity' ? 'Quantity' : name === 'unitPrice' ? 'Price' : 'Discount'}, line ${index + 1}`}
                                  aria-invalid={fieldState.invalid}
                                />
                                <FieldError errors={[fieldState.error]} />
                              </Field>
                            )}
                          />
                        </TableCell>
                      ))}
                      <TableCell className="pt-4 text-right tabular-nums">{line?.product ? formatPercent(line.product.gstRate) : '—'}</TableCell>
                      <TableCell className="pt-4 text-right tabular-nums">{result ? formatINR(result.taxableValue) : '—'}</TableCell>
                      <TableCell className="pt-4 text-right tabular-nums">{result ? formatINR(result.lineTotal) : '—'}</TableCell>
                      {editable && (
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
                      )}
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>
          <FieldError errors={[formState.errors.lines?.root ?? (formState.errors.lines?.message ? formState.errors.lines : undefined)]} />

          <div className="flex flex-wrap items-start justify-between gap-6">
            {editable ? (
              <Button type="button" variant="outline" size="sm" onClick={() => lines.append(emptyLine)} disabled={lines.fields.length >= 200}>
                <Plus />
                Add product
              </Button>
            ) : (
              <span />
            )}
            <div className="flex min-w-72 flex-col gap-1.5 text-sm">
              <TotalsRow label="Taxable value" value={formatINR(preview.taxableValue)} />
              {isInterState ? (
                <TotalsRow label="IGST" value={formatINR(preview.igstAmount)} />
              ) : (
                <>
                  <TotalsRow label="CGST" value={formatINR(preview.cgstAmount)} />
                  <TotalsRow label={usesUtgst ? 'UTGST' : 'SGST'} value={formatINR(preview.sgstAmount)} />
                </>
              )}
              <TotalsRow label="Round off" value={formatINR(preview.roundOff)} />
              <TotalsRow label="Total" value={formatINR(preview.grandTotal)} strong />
              {o && !dirty && <p className="text-xs text-muted-foreground">{o.amountInWords}</p>}
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Action bar: what's possible depends on the order's status. */}
      <div className="sticky bottom-0 -mx-4 flex flex-wrap items-center justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        {o && dirty && editable && <span className="mr-auto text-sm text-muted-foreground">Save your changes before quoting or confirming.</span>}
        {o && canEdit && (status === 'Draft' || status === 'Quoted' || status === 'Confirmed') && (
          <Button type="button" variant="ghost" className="text-destructive" disabled={busy} onClick={() => setCancelOpen(true)}>
            <Ban />
            Cancel order
          </Button>
        )}
        {o && status !== 'Cancelled' && (
          <Button type="button" variant="outline" asChild>
            <Link to={`/orders/${o.id}/print`} target="_blank">
              <Printer />
              {status === 'Draft' || status === 'Quoted' ? 'Print quotation' : 'Print order'}
            </Link>
          </Button>
        )}
        {o && canEdit && status === 'Draft' && (
          <Button
            type="button"
            variant="outline"
            disabled={busy || dirty}
            onClick={() => runAction(() => quote.mutateAsync({ id: o.id, rowVersion: o.rowVersion }), `Marked as quoted`)}
          >
            <Send />
            Mark as quoted
          </Button>
        )}
        {o && canEdit && (status === 'Draft' || status === 'Quoted') && (
          <Button type="button" variant="outline" disabled={busy || dirty} onClick={() => doConfirm(false)}>
            <CheckCircle2 />
            Confirm order
          </Button>
        )}
        {o && status === 'Confirmed' && can(Permissions.InvoicesEdit) && (
          <Button type="button" onClick={() => setInvoiceOpen(true)}>
            <ReceiptIndianRupee />
            Issue invoice
          </Button>
        )}
        {o && status === 'Invoiced' && can(Permissions.InvoicesView) && (
          <Button type="button" asChild>
            <Link to={`/invoices/${o.invoiceId}`}>
              <FileText />
              View invoice
            </Link>
          </Button>
        )}
        {editable && (
          <Button type="submit" disabled={busy || (!isNew && !dirty)}>
            {save.isPending && <Spinner />}
            {isNew ? 'Create draft' : 'Save changes'}
          </Button>
        )}
      </div>
    </form>

      {/* Dialogs sit outside the <form>: React bubbles events from portals through the component tree,
          so a dialog's own submit would otherwise also submit (save) the order. */}
      <ConfirmDialog
        open={creditProblem !== null}
        onOpenChange={(open) => !open && setCreditProblem(null)}
        title="Over the credit limit"
        description={creditProblem ?? ''}
        confirmLabel={canOverride ? 'Confirm anyway' : 'OK'}
        destructive={canOverride}
        onConfirm={() => {
          setCreditProblem(null)
          if (canOverride) void doConfirm(true)
        }}
      />
      {o && (
        <ReasonDialog
          open={cancelOpen}
          onOpenChange={setCancelOpen}
          title={`Cancel ${o.number}?`}
          description="The order stays on record as cancelled. This can't be undone."
          confirmLabel="Cancel order"
          onConfirm={async (reason) => {
            await runAction(() => cancel.mutateAsync({ id: o.id, rowVersion: o.rowVersion, reason }), `Order ${o.number} cancelled`)
            setCancelOpen(false)
          }}
        />
      )}
      {o && status === 'Confirmed' && <CreateInvoiceDialog order={o} open={invoiceOpen} onOpenChange={setInvoiceOpen} />}
    </>
  )
}
