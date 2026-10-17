import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Ban, CheckCircle2, FilePlus2, Lock, PackageCheck, Plus, Printer, Send, Trash2, Undo2 } from 'lucide-react'
import { useEffect, useId, useMemo, useState } from 'react'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { FormInput, FormTextarea } from '@/components/form/fields'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { ProductPicker } from '@/components/ProductPicker'
import { ReasonDialog } from '@/components/ReasonDialog'
import { SupplierPicker } from '@/components/SupplierPicker'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { RecordHistory } from '@/features/audit/RecordHistory'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, formatQuantity, todayInIndia } from '@/lib/format'
import { calculateTotals } from '@/lib/gst'
import { cn } from '@/lib/utils'
import {
  useApprovePurchaseOrder,
  useCancelPurchaseOrder,
  useClosePurchaseOrder,
  usePlacePurchaseOrder,
  usePurchaseOrder,
  useRejectPurchaseOrder,
  useSavePurchaseOrder,
  useSupplier,
  type PurchaseOrder,
} from './api'
import { BillStatusBadge, MatchBadge, PurchaseOrderStatusBadge } from './badges'
import { emptyLine, emptyPurchaseOrderForm, purchaseOrderFormSchema, toFormPath, toFormValues, toSaveRequest, type PurchaseOrderFormValues } from './purchase-order-form'
import { ReceiveGoodsDialog } from './ReceiveGoodsDialog'

function TotalsRow({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={cn('flex justify-between gap-8 tabular-nums', strong && 'border-t pt-2 text-base font-semibold')}>
      <span className={strong ? '' : 'text-muted-foreground'}>{label}</span>
      <span>{value}</span>
    </div>
  )
}

type Pending = 'reject' | 'cancel' | 'close' | null

export function PurchaseOrderFormPage() {
  const { id: idParam } = useParams()
  const id = idParam ? Number(idParam) : undefined
  const isNew = id === undefined
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { can } = useAuth()
  const canEdit = can(Permissions.PurchaseOrdersEdit)
  const canApprove = can(Permissions.PurchaseOrdersApprove)
  const canReceive = can(Permissions.StockEdit)
  const canBill = can(Permissions.SupplierBillsEdit)
  const supplierPickerId = useId()

  const order = usePurchaseOrder(id)
  // A new order can start from a supplier's page (?supplierId=...).
  const startingSupplier = useSupplier(isNew && searchParams.get('supplierId') ? Number(searchParams.get('supplierId')) : undefined)

  const save = useSavePurchaseOrder()
  const place = usePlacePurchaseOrder()
  const approve = useApprovePurchaseOrder()
  const reject = useRejectPurchaseOrder()
  const cancel = useCancelPurchaseOrder()
  const close = useClosePurchaseOrder()

  const [pending, setPending] = useState<Pending>(null)
  const [receiveOpen, setReceiveOpen] = useState(false)
  const [createdId, setCreatedId] = useState<number | null>(null)
  useEffect(() => {
    if (createdId) navigate(`/purchase-orders/${createdId}`, { replace: true })
  }, [createdId, navigate])

  const form = useForm<PurchaseOrderFormValues>({ resolver: zodResolver(purchaseOrderFormSchema), defaultValues: emptyPurchaseOrderForm() })
  const { control, formState, reset } = form
  const lines = useFieldArray({ control, name: 'lines' })

  useEffect(() => {
    if (order.data) reset(toFormValues(order.data))
  }, [order.data, reset])

  useEffect(() => {
    const s = startingSupplier.data
    if (s && !form.getValues('supplier')) {
      form.setValue('supplier', { id: s.id, code: s.code, name: s.name, gstin: s.gstin, city: s.address.city, stateName: s.address.stateName }, { shouldDirty: true })
    }
  }, [startingSupplier.data, form])

  const status = order.data?.status
  const editable = canEdit && (isNew || status === 'Draft')
  const dirty = formState.isDirty

  // ---- Live GST preview: IGST when the supplier is in another state.
  const [supplier, watchedLines] = useWatch({ control, name: ['supplier', 'lines'] })
  const supplierDetail = useSupplier(supplier?.id)
  const isInterState = supplierDetail.data?.isInterState ?? order.data?.isInterState ?? false
  const preview = useMemo(
    () =>
      calculateTotals(
        watchedLines.map((l) => ({ quantity: Number(l.quantity) || 0, unitPrice: Number(l.unitPrice) || 0, discountPercent: 0, gstRate: l.product?.gstRate ?? 0 })),
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
      toast.success(isNew ? `Purchase order ${saved.number} created` : 'Order saved')
      if (isNew) setCreatedId(saved.id)
    } catch (error) {
      applyServerErrors(error)
    }
  })

  const runAction = async (action: () => Promise<PurchaseOrder>, success: (o: PurchaseOrder) => string) => {
    try {
      const updated = await action()
      reset(toFormValues(updated))
      toast.success(success(updated))
      return true
    } catch (error) {
      toast.error(errorMessage(error))
      return false
    }
  }

  if (!isNew && order.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (!isNew && order.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(order.error)}</AlertDescription>
      </Alert>
    )
  }

  const o = order.data
  const placed = !!o && o.status !== 'Draft' && o.status !== 'PendingApproval' && o.status !== 'Cancelled'
  const receivable = o?.status === 'Ordered' || o?.status === 'PartlyReceived'
  const unbilled = !!o && o.lines.some((l) => l.quantityReceived > l.quantityBilled)
  const busy = save.isPending || place.isPending || approve.isPending || reject.isPending || cancel.isPending || close.isPending

  return (
    <>
      <form onSubmit={onSave} noValidate className="flex max-w-6xl flex-col gap-6">
        <UnsavedChangesGuard when={dirty && createdId === null} />

        <PageHeader
          title={isNew ? 'New purchase order' : (o?.number ?? 'Purchase order')}
          description={
            o ? (
              <span className="inline-flex flex-wrap items-center gap-2">
                <PurchaseOrderStatusBadge status={o.status} late={o.isLate} />
                {o.approvedByName && <span className="text-xs">Approved by {o.approvedByName} on {formatDate(o.approvedAtUtc)}</span>}
              </span>
            ) : (
              'Start as a draft, then place it with the supplier. Orders above the approval limit wait for approval.'
            )
          }
          actions={
            <Button variant="ghost" asChild>
              <Link to="/purchase-orders">
                <ArrowLeft />
                Back to orders
              </Link>
            </Button>
          }
        />

        {o?.status === 'Cancelled' && (
          <Alert variant="destructive">
            <Ban />
            <AlertDescription>
              Cancelled {formatDate(o.cancelledAtUtc)}: {o.cancellationReason}
            </AlertDescription>
          </Alert>
        )}
        {o?.status === 'Closed' && (
          <Alert>
            <Lock />
            <AlertDescription>
              Closed {formatDate(o.closedAtUtc)}: {o.closeReason}. Nothing more will be received against it.
            </AlertDescription>
          </Alert>
        )}
        {o?.status === 'Draft' && o.rejectionReason && (
          <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
            <Undo2 />
            <AlertDescription className="text-inherit">Sent back for changes: {o.rejectionReason}</AlertDescription>
          </Alert>
        )}
        {o?.status === 'PendingApproval' && (
          <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
            <Send />
            <AlertDescription className="text-inherit">
              Its total of {formatINR(o.grandTotal, 0)} is above the approval limit of {formatINR(o.approvalLimit, 0)}, so it waits for someone who can approve
              purchase orders before it goes to the supplier.
            </AlertDescription>
          </Alert>
        )}

        <Card>
          <CardHeader>
            <CardTitle>Supplier & dates</CardTitle>
            {supplierDetail.data && (
              <CardDescription>
                {supplierDetail.data.address.city}, {supplierDetail.data.address.stateName} —{' '}
                {isInterState ? 'another state, so their bill carries IGST' : 'your state, so their bill carries CGST + SGST'}
                {supplierDetail.data.payable > 0 && ` · we owe them ${formatINR(supplierDetail.data.payable, 0)}`}
              </CardDescription>
            )}
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <Controller
                control={control}
                name="supplier"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid} className="sm:col-span-2">
                    <FieldLabel htmlFor={supplierPickerId}>Supplier</FieldLabel>
                    <SupplierPicker id={supplierPickerId} value={field.value} onChange={field.onChange} disabled={!editable} invalid={fieldState.invalid} />
                    <FieldError errors={[fieldState.error]} />
                  </Field>
                )}
              />
              <FormInput control={control} name="orderDate" label="Order date" type="date" max={todayInIndia()} disabled={!editable} />
              <FormInput control={control} name="expectedDate" label="Expected delivery" type="date" disabled={!editable} />
              <FormInput control={control} name="supplierReference" label="Their quotation / ref" placeholder="Optional" disabled={!editable} className="sm:col-span-2" />
              <FormTextarea control={control} name="notes" label="Notes" rows={1} disabled={!editable} className="sm:col-span-2" />
            </FieldGroup>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Items</CardTitle>
            <CardDescription>
              {editable
                ? "Prices exclude GST. Leave a price blank to use what you last paid this supplier (or the product's cost)."
                : 'Prices exclude GST. Goods are received into stock at these prices.'}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <div className="overflow-x-auto rounded-lg border">
              <Table stack>
                <TableHeader className="bg-muted/50">
                  <TableRow>
                    <TableHead className="min-w-64">Product</TableHead>
                    <TableHead className="w-32">Quantity</TableHead>
                    <TableHead className="w-32">Price (₹)</TableHead>
                    <TableHead className="w-16 text-right">GST</TableHead>
                    <TableHead className="w-32 text-right">Total</TableHead>
                    {placed && <TableHead className="w-28 text-right">Received</TableHead>}
                    {placed && <TableHead className="w-28 text-right">Billed</TableHead>}
                    {editable && <TableHead className="w-12" />}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {lines.fields.map((row, index) => {
                    const line = watchedLines[index]
                    const result = preview.lines[index]
                    const saved = o?.lines[index]
                    return (
                      <TableRow key={row.id} className="align-top hover:bg-transparent">
                        <TableCell>
                          <Controller
                            control={control}
                            name={`lines.${index}.product`}
                            render={({ field, fieldState }) => (
                              <Field data-invalid={fieldState.invalid}>
                                <ProductPicker value={field.value} disabled={!editable} excludeIds={pickedIds} invalid={fieldState.invalid} onChange={field.onChange} />
                                {line?.product && (
                                  <span className="text-xs text-muted-foreground">{formatQuantity(line.product.quantityOnHand, line.product.unit)} in stock</span>
                                )}
                                <FieldError errors={[fieldState.error]} />
                              </Field>
                            )}
                          />
                        </TableCell>
                        {(['quantity', 'unitPrice'] as const).map((name) => (
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
                                    placeholder={name === 'unitPrice' ? 'Last price' : undefined}
                                    aria-label={`${name === 'quantity' ? 'Quantity' : 'Price'}, line ${index + 1}`}
                                    aria-invalid={fieldState.invalid}
                                  />
                                  <FieldError errors={[fieldState.error]} />
                                </Field>
                              )}
                            />
                          </TableCell>
                        ))}
                        <TableCell className="pt-4 text-right tabular-nums">{line?.product ? formatPercent(line.product.gstRate) : '—'}</TableCell>
                        <TableCell className="pt-4 text-right tabular-nums">{result && Number(line?.unitPrice) > 0 ? formatINR(result.lineTotal) : '—'}</TableCell>
                        {placed && (
                          <TableCell className="pt-4 text-right tabular-nums">
                            {saved ? formatQuantity(saved.quantityReceived) : '—'}
                            {saved && saved.quantityPending > 0 && <span className="block text-xs text-muted-foreground">{formatQuantity(saved.quantityPending)} to come</span>}
                          </TableCell>
                        )}
                        {placed && <TableCell className="pt-4 text-right tabular-nums">{saved ? formatQuantity(saved.quantityBilled) : '—'}</TableCell>}
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
                    <TotalsRow label="SGST" value={formatINR(preview.sgstAmount)} />
                  </>
                )}
                <TotalsRow label="Round off" value={formatINR(preview.roundOff)} />
                <TotalsRow label="Total" value={formatINR(preview.grandTotal)} strong />
                {o && !dirty && <p className="text-xs text-muted-foreground">{o.amountInWords}</p>}
                {o?.status === 'Draft' && !dirty && o.needsApproval && !canApprove && (
                  <p className="text-xs text-amber-700 dark:text-amber-400">Above the approval limit of {formatINR(o.approvalLimit, 0)}: placing it sends it for approval.</p>
                )}
              </div>
            </div>
          </CardContent>
        </Card>

        {o && (o.receipts.length > 0 || o.bills.length > 0) && (
          <div className="grid gap-6 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>Deliveries</CardTitle>
                <CardDescription>Goods received against this order.</CardDescription>
              </CardHeader>
              <CardContent>
                {o.receipts.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Nothing received yet.</p>
                ) : (
                  <ul className="flex flex-col divide-y text-sm">
                    {o.receipts.map((r) => (
                      <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                        <Link to={`/stock/receipts/${r.id}`} className="font-mono text-xs underline underline-offset-4">
                          {r.number}
                        </Link>
                        <span className="text-muted-foreground">
                          {formatBusinessDate(r.receiptDate)}
                          {r.supplierReference && ` · ${r.supplierReference}`} · {r.lineCount} item{r.lineCount === 1 ? '' : 's'} · {r.createdByName}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>Bills</CardTitle>
                <CardDescription>The supplier's invoices for this order.</CardDescription>
              </CardHeader>
              <CardContent>
                {o.bills.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No bill entered yet.</p>
                ) : (
                  <ul className="flex flex-col divide-y text-sm">
                    {o.bills.map((b) => (
                      <li key={b.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                        <span className="flex flex-col">
                          <Link to={`/supplier-bills/${b.id}`} className="font-mono text-xs underline underline-offset-4">
                            {b.number}
                          </Link>
                          <span className="text-xs text-muted-foreground">
                            Their {b.supplierInvoiceNumber} · {formatBusinessDate(b.billDate)} · {formatINR(b.grandTotal)}
                          </span>
                        </span>
                        <span className="inline-flex flex-wrap gap-1">
                          <MatchBadge status={b.matchStatus} />
                          <BillStatusBadge status={b.status} />
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>
          </div>
        )}

        {o && <RecordHistory entityType="Purchase order" entityId={o.id} />}

        {/* Action bar: what's possible depends on the order's status. */}
        <div className="sticky bottom-0 -mx-4 flex flex-wrap items-center justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
          {o && dirty && editable && <span className="mr-auto text-sm text-muted-foreground">Save your changes before placing the order.</span>}
          {o && canEdit && (status === 'Draft' || status === 'PendingApproval' || (status === 'Ordered' && o.receipts.length === 0)) && (
            <Button type="button" variant="ghost" className="text-destructive" disabled={busy} onClick={() => setPending('cancel')}>
              <Ban />
              Cancel order
            </Button>
          )}
          {o && canEdit && status === 'PartlyReceived' && (
            <Button type="button" variant="ghost" disabled={busy} onClick={() => setPending('close')}>
              <Lock />
              Close short
            </Button>
          )}
          {o && status !== 'Cancelled' && (
            <Button type="button" variant="outline" asChild>
              <Link to={`/purchase-orders/${o.id}/print`} target="_blank">
                <Printer />
                Print
              </Link>
            </Button>
          )}
          {o && canApprove && status === 'PendingApproval' && (
            <>
              <Button type="button" variant="outline" disabled={busy} onClick={() => setPending('reject')}>
                <Undo2 />
                Send back
              </Button>
              <Button
                type="button"
                disabled={busy}
                onClick={() => runAction(() => approve.mutateAsync({ id: o.id, rowVersion: o.rowVersion }), (u) => `${u.number} approved and ordered`)}
              >
                <CheckCircle2 />
                Approve
              </Button>
            </>
          )}
          {o && canEdit && status === 'Draft' && (
            <Button
              type="button"
              variant="outline"
              disabled={busy || dirty}
              onClick={() =>
                runAction(
                  () => place.mutateAsync({ id: o.id, rowVersion: o.rowVersion }),
                  (u) => (u.status === 'PendingApproval' ? `${u.number} sent for approval` : `${u.number} placed with ${u.supplierName}`),
                )
              }
            >
              <Send />
              Place order
            </Button>
          )}
          {o && canBill && placed && unbilled && (
            <Button type="button" variant="outline" asChild>
              <Link to={`/supplier-bills/new?purchaseOrderId=${o.id}`}>
                <FilePlus2 />
                Enter bill
              </Link>
            </Button>
          )}
          {o && canReceive && receivable && (
            <Button type="button" onClick={() => setReceiveOpen(true)}>
              <PackageCheck />
              Receive goods
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

      {/* Dialogs sit outside the <form>: events from portals bubble through the component tree. */}
      {o && (
        <ReasonDialog
          open={pending !== null}
          onOpenChange={(open) => !open && setPending(null)}
          title={pending === 'reject' ? `Send ${o.number} back?` : pending === 'close' ? `Close ${o.number}?` : `Cancel ${o.number}?`}
          description={
            pending === 'reject'
              ? 'It goes back to draft with your reason, so it can be changed and placed again.'
              : pending === 'close'
                ? "Nothing more will be received against it. What has arrived stays in stock and can still be billed."
                : "The order stays on record as cancelled. This can't be undone."
          }
          confirmLabel={pending === 'reject' ? 'Send back' : pending === 'close' ? 'Close order' : 'Cancel order'}
          onConfirm={async (reason) => {
            const args = { id: o.id, rowVersion: o.rowVersion, reason }
            const ok =
              pending === 'reject'
                ? await runAction(() => reject.mutateAsync(args), (u) => `${u.number} sent back to draft`)
                : pending === 'close'
                  ? await runAction(() => close.mutateAsync(args), (u) => `${u.number} closed`)
                  : await runAction(() => cancel.mutateAsync(args), (u) => `${u.number} cancelled`)
            if (ok) setPending(null)
          }}
        />
      )}
      {o && receivable && <ReceiveGoodsDialog order={o} open={receiveOpen} onOpenChange={setReceiveOpen} />}
    </>
  )
}
