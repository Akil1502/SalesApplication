import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Sparkles, TriangleAlert } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSelect, FormTextarea } from '@/components/form/fields'
import { moneyString, toNumberOrNull } from '@/components/form/schemas'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatINR, formatQuantity, todayInIndia } from '@/lib/format'
import { calculateTotals } from '@/lib/gst'
import { cn } from '@/lib/utils'
import { useCreateSupplierBill, usePurchaseOrder, type BillExtraction, type PurchaseOrder } from './api'
import { ReadBillCard } from './ReadBillCard'
import { msg, t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'

const gstRates = ['0', '5', '12', '18', '28'].map((r) => ({ value: r, label: `${r}%` }))

const schema = z
  .object({
    supplierInvoiceNumber: z.string().trim().min(1, { error: msg("Enter the supplier's invoice number.") }).max(30),
    billDate: z.string().min(1, { error: msg('Choose a date.') }),
    dueDate: z.string(),
    lines: z.array(
      z.object({
        purchaseOrderLineId: z.number(),
        quantity: z.string().trim().regex(/^(\d+(\.\d{1,3})?)?$/, { error: msg('Up to 3 decimals.') }),
        unitPrice: z.string().trim().regex(/^\d+(\.\d{1,2})?$/, { error: msg('Enter the rate.') }),
        gstRate: z.string(),
      }),
    ),
    chargesAmount: moneyString({ optional: true, max: 9_999_999 }),
    chargesGstRate: z.string(),
    chargesDescription: z.string().trim().max(100),
    supplierTotal: moneyString({ optional: true, max: 999_999_999 }),
    notes: z.string().trim().max(1000),
  })
  .superRefine((v, ctx) => {
    if (!v.lines.some((l) => Number(l.quantity) > 0)) {
      ctx.addIssue({ code: 'custom', path: ['lines'], message: t('Enter the quantity billed on at least one line.') })
    }
    if (v.dueDate && v.dueDate < v.billDate) {
      ctx.addIssue({ code: 'custom', path: ['dueDate'], message: t("Can't be before the bill date.") })
    }
  })

type Values = z.infer<typeof schema>

/** Enters a supplier's bill against a purchase order (?purchaseOrderId=…), by hand or read from the invoice with AI. */
export function SupplierBillFormPage() {
  const [searchParams] = useSearchParams()
  const orderId = Number(searchParams.get('purchaseOrderId')) || undefined
  const order = usePurchaseOrder(orderId)

  if (!orderId) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>
          <T
            text="Open the purchase order the bill is for and choose “Enter bill”, or use “Enter bill” on the {link} page."
            values={{
              link: (
                <Link to="/supplier-bills" className="underline">
                  {t('Supplier bills')}
                </Link>
              ),
            }}
          />
        </AlertDescription>
      </Alert>
    )
  }
  if (order.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (order.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(order.error)}</AlertDescription>
      </Alert>
    )
  }
  return <BillForm order={order.data} />
}

function BillForm({ order }: { order: PurchaseOrder }) {
  const navigate = useNavigate()
  const { can } = useAuth()
  const ai = useAiStatus()
  const create = useCreateSupplierBill()
  const [source, setSource] = useState<'Manual' | 'AiPdf' | 'AiImage'>('Manual')
  const [extraction, setExtraction] = useState<BillExtraction | null>(null)
  const [savedId, setSavedId] = useState<number | null>(null)
  // Navigate after the post-save render, so the unsaved-changes guard sees the form as saved.
  useEffect(() => {
    if (savedId !== null) navigate(`/supplier-bills/${savedId}`)
  }, [savedId, navigate])

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      supplierInvoiceNumber: '',
      billDate: todayInIndia(),
      dueDate: '',
      // Each line starts at what has arrived but isn't billed yet, at the order's price.
      lines: order.lines.map((l) => ({
        purchaseOrderLineId: l.id,
        quantity: l.quantityReceived > l.quantityBilled ? String(Math.round((l.quantityReceived - l.quantityBilled) * 1000) / 1000) : '',
        unitPrice: l.unitPrice.toFixed(2),
        gstRate: String(l.gstRate),
      })),
      chargesAmount: '',
      chargesGstRate: '18',
      chargesDescription: '',
      supplierTotal: '',
      notes: '',
    },
  })
  const { control, formState } = form
  const [lines, chargesAmount, chargesGstRate] = useWatch({ control, name: ['lines', 'chargesAmount', 'chargesGstRate'] })

  const preview = useMemo(() => {
    const items = lines.map((l) => ({ quantity: Number(l.quantity) || 0, unitPrice: Number(l.unitPrice) || 0, discountPercent: 0, gstRate: Number(l.gstRate) || 0 }))
    if (Number(chargesAmount) > 0) items.push({ quantity: 1, unitPrice: Number(chargesAmount), discountPercent: 0, gstRate: Number(chargesGstRate) || 0 })
    return calculateTotals(items, order.isInterState)
  }, [lines, chargesAmount, chargesGstRate, order.isInterState])

  /** Fills the form from what the AI read; lines it couldn't match are listed for the clerk instead. */
  const applyExtraction = (read: BillExtraction) => {
    setExtraction(read)
    setSource(read.source === 'AiImage' ? 'AiImage' : 'AiPdf')
    const set = (name: Parameters<typeof form.setValue>[0], value: string) => form.setValue(name, value, { shouldDirty: true, shouldValidate: false })
    if (read.supplierInvoiceNumber) set('supplierInvoiceNumber', read.supplierInvoiceNumber)
    if (read.billDate) set('billDate', read.billDate)
    if (read.dueDate) set('dueDate', read.dueDate)
    order.lines.forEach((orderLine, index) => {
      const match = read.lines.find((l) => l.purchaseOrderLineId === orderLine.id)
      set(`lines.${index}.quantity`, match?.quantity != null ? String(match.quantity) : '')
      if (match?.unitPrice != null) set(`lines.${index}.unitPrice`, match.unitPrice.toFixed(2))
      if (match?.gstRate != null) set(`lines.${index}.gstRate`, String(match.gstRate))
    })
    set('chargesAmount', read.chargesAmount > 0 ? read.chargesAmount.toFixed(2) : '')
    if (read.chargesAmount > 0) set('chargesGstRate', String(read.chargesGstRate))
    set('chargesDescription', read.chargesDescription ?? '')
    set('supplierTotal', read.statedTotal != null ? read.statedTotal.toFixed(2) : '')
    if (read.notes) set('notes', read.notes)
  }

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const bill = await create.mutateAsync({
        purchaseOrderId: order.id,
        supplierInvoiceNumber: values.supplierInvoiceNumber,
        billDate: values.billDate,
        dueDate: values.dueDate || null,
        chargesAmount: Number(values.chargesAmount) || 0,
        chargesGstRate: Number(values.chargesGstRate) || 0,
        chargesDescription: values.chargesDescription || null,
        supplierTotal: toNumberOrNull(values.supplierTotal),
        notes: values.notes || null,
        source,
        lines: values.lines
          .filter((l) => Number(l.quantity) > 0)
          .map((l) => ({ purchaseOrderLineId: l.purchaseOrderLineId, quantity: Number(l.quantity), unitPrice: Number(l.unitPrice), gstRate: Number(l.gstRate) })),
      })
      if (bill.matchStatus === 'NeedsApproval')
        toast.warning(t("Bill {number} saved. It doesn't match the order, so it needs approval before it can be paid.", { number: bill.number }))
      else toast.success(t('Bill {number} saved; it matches the order', { number: bill.number }))
      setSavedId(bill.id)
    } catch (error) {
      if (error instanceof ApiRequestError && error.body.errors) {
        const included = values.lines.map((l, i) => ({ l, i })).filter(({ l }) => Number(l.quantity) > 0)
        for (const [key, messages] of Object.entries(error.body.errors)) {
          // Server line indexes count only the lines sent; map them back to the form's rows.
          const path = key.replace(/^lines\[(\d+)\]\.(\w+)$/, (_, n: string, field: string) => `lines.${included[Number(n)]?.i ?? n}.${field === 'purchaseOrderLineId' ? 'quantity' : field}`)
          form.setError(path as Parameters<typeof form.setError>[0], { type: 'server', message: messages[0] })
        }
      }
      toast.error(errorMessage(error))
    }
  })

  const canRead = can(Permissions.BillReadingUse) && !!ai.data?.billReading

  return (
    <form onSubmit={onSubmit} noValidate className="flex max-w-6xl flex-col gap-6">
      <UnsavedChangesGuard when={formState.isDirty && savedId === null} />
      <PageHeader
        title={t('Enter supplier bill')}
        description={
          <T
            text="For {supplier}, against {order} ({tax}). It's checked against the order's prices and what arrived when you save."
            values={{
              supplier: order.supplierName,
              order: (
                <Link to={`/purchase-orders/${order.id}`} className="font-mono underline underline-offset-4">
                  {order.number}
                </Link>
              ),
              tax: order.isInterState ? 'IGST' : 'CGST + SGST',
            }}
          />
        }
        actions={
          <Button variant="ghost" asChild>
            <Link to={`/purchase-orders/${order.id}`}>
              <ArrowLeft />
              {t('Back to the order')}
            </Link>
          </Button>
        }
      />

      {canRead && <ReadBillCard purchaseOrderId={order.id} onRead={applyExtraction} />}

      {extraction && (extraction.warnings.length > 0 || extraction.unmatched.length > 0 || extraction.lines.some((l) => l.purchaseOrderLineId === null)) && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <TriangleAlert />
          <AlertDescription className="text-inherit">
            <p className="font-medium">{t('Check these before saving:')}</p>
            <ul className="mt-1 ml-4 list-disc">
              {extraction.warnings.map((w) => (
                <li key={w}>{w}</li>
              ))}
              {extraction.lines
                .filter((l) => l.purchaseOrderLineId === null)
                .map((l) => (
                  <li key={l.sourceText}>
                    {l.quantity != null && l.unitPrice != null
                      ? t('Not on the order: “{text}” ({quantity} at {price}). Left off the bill.', {
                          text: l.sourceText,
                          quantity: formatQuantity(l.quantity),
                          price: formatINR(l.unitPrice),
                        })
                      : l.quantity != null
                        ? t('Not on the order: “{text}” ({quantity}). Left off the bill.', { text: l.sourceText, quantity: formatQuantity(l.quantity) })
                        : l.unitPrice != null
                          ? t('Not on the order: “{text}” at {price}. Left off the bill.', { text: l.sourceText, price: formatINR(l.unitPrice) })
                          : t('Not on the order: “{text}”. Left off the bill.', { text: l.sourceText })}
                  </li>
                ))}
              {extraction.unmatched.map((u) => (
                <li key={u}>{t("Couldn't read: “{text}”", { text: u })}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            {t("Supplier's invoice")}
            {source !== 'Manual' && <Sparkles className="size-4 text-violet-600" aria-label={t('Filled in by AI')} />}
          </CardTitle>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-3">
            <FormInput control={control} name="supplierInvoiceNumber" label={t('Their invoice number')} maxLength={30} />
            <FormInput control={control} name="billDate" label={t('Invoice date')} type="date" max={todayInIndia()} />
            <FormInput control={control} name="dueDate" label={t('Due date')} type="date" description={t('Blank: the invoice date plus their payment terms.')} />
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Items')}</CardTitle>
          <CardDescription>{t("As charged on their invoice, before GST. Leave a line empty if it isn't on this bill.")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <div className="overflow-x-auto rounded-lg border">
            <Table stack>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead className="min-w-56">{t('Product')}</TableHead>
                  <TableHead className="text-right">{t('Received')}</TableHead>
                  <TableHead className="text-right">{t('Billed before')}</TableHead>
                  <TableHead className="w-32">{t('Quantity')}</TableHead>
                  <TableHead className="w-32">{t('Rate (₹)')}</TableHead>
                  <TableHead className="w-28">GST</TableHead>
                  <TableHead className="w-32 text-right">{t('Total')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {order.lines.map((orderLine, index) => {
                  const line = lines[index]
                  const quantity = Number(line?.quantity) || 0
                  const price = Number(line?.unitPrice) || 0
                  const unbilled = orderLine.quantityReceived - orderLine.quantityBilled
                  const overBilled = quantity > 0 && quantity > unbilled
                  const priceDiff = orderLine.unitPrice > 0 && quantity > 0 ? ((price - orderLine.unitPrice) / orderLine.unitPrice) * 100 : 0
                  return (
                    <TableRow key={orderLine.id} className="align-top hover:bg-transparent">
                      <TableCell>
                        <span className="font-mono text-xs">{orderLine.sku}</span>
                        <span className="block text-sm">{orderLine.productName}</span>
                      </TableCell>
                      <TableCell className="pt-4 text-right tabular-nums">{formatQuantity(orderLine.quantityReceived, orderLine.unit)}</TableCell>
                      <TableCell className="pt-4 text-right tabular-nums">{formatQuantity(orderLine.quantityBilled)}</TableCell>
                      <TableCell>
                        <Controller
                          control={control}
                          name={`lines.${index}.quantity`}
                          render={({ field, fieldState }) => (
                            <Field data-invalid={fieldState.invalid}>
                              <Input {...field} inputMode="decimal" placeholder="0" aria-label={t('Quantity billed for {sku}', { sku: orderLine.sku })} aria-invalid={fieldState.invalid} />
                              {overBilled && (
                                <span className="text-xs text-amber-700 dark:text-amber-400">
                                  {t('More than has arrived unbilled ({quantity})', { quantity: formatQuantity(unbilled) })}
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
                          name={`lines.${index}.unitPrice`}
                          render={({ field, fieldState }) => (
                            <Field data-invalid={fieldState.invalid}>
                              <Input {...field} inputMode="decimal" aria-label={t('Rate for {sku}', { sku: orderLine.sku })} aria-invalid={fieldState.invalid} />
                              {Math.abs(priceDiff) >= 0.01 && (
                                <span className={cn('text-xs', Math.abs(priceDiff) > 2 ? 'text-amber-700 dark:text-amber-400' : 'text-muted-foreground')}>
                                  {priceDiff > 0
                                    ? t("{percent}% above the order's {price}", { percent: Math.abs(priceDiff).toFixed(1), price: formatINR(orderLine.unitPrice) })
                                    : t("{percent}% below the order's {price}", { percent: Math.abs(priceDiff).toFixed(1), price: formatINR(orderLine.unitPrice) })}
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
                          name={`lines.${index}.gstRate`}
                          render={({ field }) => (
                            <Select value={field.value} onValueChange={field.onChange}>
                              <SelectTrigger className="w-full" aria-label={t('GST rate for {sku}', { sku: orderLine.sku })}>
                                <SelectValue />
                              </SelectTrigger>
                              <SelectContent>
                                {gstRates.map((r) => (
                                  <SelectItem key={r.value} value={r.value}>
                                    {r.label}
                                  </SelectItem>
                                ))}
                              </SelectContent>
                            </Select>
                          )}
                        />
                      </TableCell>
                      <TableCell className="pt-4 text-right tabular-nums">{quantity > 0 ? formatINR(preview.lines[index]?.lineTotal) : '—'}</TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>
          <FieldError errors={[formState.errors.lines?.root ?? (formState.errors.lines?.message ? formState.errors.lines : undefined)]} />
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-[1fr_auto]">
        <Card>
          <CardHeader>
            <CardTitle>{t('Charges and totals')}</CardTitle>
            <CardDescription>{t('Freight, loading and the like go into the cost of the goods. The total from their invoice is checked against ours.')}</CardDescription>
          </CardHeader>
          <CardContent>
            <FieldGroup className="grid gap-4 sm:grid-cols-3">
              <FormInput control={control} name="chargesAmount" label={t('Charges (₹, before GST)')} inputMode="decimal" placeholder="0" />
              <FormSelect control={control} name="chargesGstRate" label={t('GST on charges')} options={gstRates} />
              <FormInput control={control} name="chargesDescription" label={t('Charges for')} placeholder={t('Freight')} />
              <FormInput control={control} name="supplierTotal" label={t('Total on their invoice (₹)')} inputMode="decimal" placeholder={t('Optional')} />
              <FormTextarea control={control} name="notes" label={t('Notes')} rows={1} className="sm:col-span-2" />
            </FieldGroup>
          </CardContent>
        </Card>
        <Card className="min-w-72">
          <CardContent className="flex flex-col gap-1.5 text-sm tabular-nums">
            {[
              [t('Taxable value'), preview.taxableValue],
              ...(order.isInterState ? [['IGST', preview.igstAmount]] : [['CGST', preview.cgstAmount], ['SGST', preview.sgstAmount]]),
              [t('Round off'), preview.roundOff],
            ].map(([label, value]) => (
              <div key={label as string} className="flex justify-between gap-8">
                <span className="text-muted-foreground">{label}</span>
                <span>{formatINR(value as number)}</span>
              </div>
            ))}
            <div className="flex justify-between gap-8 border-t pt-2 text-base font-semibold">
              <span>{t('Total')}</span>
              <span>{formatINR(preview.grandTotal)}</span>
            </div>
            <p className="text-xs text-muted-foreground">{t('Order {number} dated {date}', { number: order.number, date: formatBusinessDate(order.orderDate) })}</p>
          </CardContent>
        </Card>
      </div>

      <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        <Button type="button" variant="outline" onClick={() => navigate(`/purchase-orders/${order.id}`)}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={create.isPending}>
          {create.isPending && <Spinner />}
          {t('Save bill')}
        </Button>
      </div>
    </form>
  )
}
