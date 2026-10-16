import { ArrowLeft, Info } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useInvoice, type Invoice } from '@/features/sales/api'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatINR, formatQuantity, todayInIndia } from '@/lib/format'
import { useDebounced } from '@/lib/use-debounced'
import { useCreateCreditNote, useCreditNotePreview, type CreditNoteCreateRequest, type CreditNoteKind, type ReturnDisposition, type ReturnReason } from './api'
import { dispositionLabels, goodsReasons, priceReasons, returnReasonLabels } from './labels'

/** Issues a credit note from an invoice: goods coming back, or a correction of the value with no goods moving. */
export function CreditNoteFormPage() {
  const invoice = useInvoice(Number(useParams().id))

  if (invoice.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (invoice.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(invoice.error)}</AlertDescription>
      </Alert>
    )
  }

  if (invoice.data.status === 'Cancelled') {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{invoice.data.number} is cancelled, so there's nothing to credit.</AlertDescription>
      </Alert>
    )
  }

  return <CreditNoteForm invoice={invoice.data} />
}

function CreditNoteForm({ invoice }: { invoice: Invoice }) {
  const navigate = useNavigate()
  const create = useCreateCreditNote()
  const [kind, setKind] = useState<CreditNoteKind>('GoodsReturn')
  const [reason, setReason] = useState<ReturnReason>('QualityIssue')
  const [reasonNote, setReasonNote] = useState('')
  const [date, setDate] = useState(todayInIndia())
  const [quantities, setQuantities] = useState<Record<number, string>>({})
  const [dispositions, setDispositions] = useState<Record<number, ReturnDisposition>>({})
  const [amounts, setAmounts] = useState<Record<number, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState<number | null>(null)

  // Leave only once the saved state has rendered, so the unsaved-changes guard lets go first.
  useEffect(() => {
    if (saved !== null) navigate(`/credit-notes/${saved}`)
  }, [saved, navigate])

  const goods = kind === 'GoodsReturn'
  const lines = invoice.lines.map((l) => ({ ...l, quantityLeft: l.quantity - l.quantityReturned, valueLeft: Math.round((l.taxableValue - l.valueCredited) * 100) / 100 }))
  const raw = (lineId: number) => ((goods ? quantities[lineId] : amounts[lineId]) ?? '').trim()
  const problem = (line: (typeof lines)[number]): string | null => {
    const value = raw(line.id)
    if (value === '') return null
    if (goods) {
      if (!/^\d+(\.\d{1,3})?$/.test(value)) return 'Up to 3 decimals.'
      if (Number(value) > line.quantityLeft) return line.quantityLeft > 0 ? `At most ${formatQuantity(line.quantityLeft, line.unit)}.` : 'All returned already.'
    } else {
      if (!/^\d+(\.\d{1,2})?$/.test(value)) return 'Up to 2 decimals.'
      if (Number(value) > line.valueLeft) return line.valueLeft > 0 ? `At most ${formatINR(line.valueLeft)}.` : 'Fully credited already.'
    }

    return null
  }

  const entered = lines.filter((l) => Number(raw(l.id)) > 0)
  const invalid = lines.some((l) => problem(l) !== null)
  const request: CreditNoteCreateRequest | null =
    entered.length === 0 || invalid
      ? null
      : {
          invoiceId: invoice.id,
          creditNoteDate: date,
          kind,
          reason,
          reasonNote: reasonNote.trim() || null,
          lines: entered.map((l) =>
            goods
              ? { invoiceLineId: l.id, quantity: Number(raw(l.id)), disposition: dispositions[l.id] ?? 'Restock', amount: null }
              : { invoiceLineId: l.id, amount: Number(raw(l.id)), quantity: null, disposition: null },
          ),
        }

  // The preview is worked out by the server exactly as saving would, once typing pauses.
  const requestKey = useDebounced(JSON.stringify(request), 350)
  const previewRequest = useMemo(() => JSON.parse(requestKey) as CreditNoteCreateRequest | null, [requestKey])
  const preview = useCreditNotePreview(previewRequest)
  const p = request ? preview.data : undefined
  const afterDeadline = date > invoice.gstCreditDeadline

  const changeKind = (next: CreditNoteKind) => {
    setKind(next)
    setReason(next === 'GoodsReturn' ? 'QualityIssue' : 'RateDifference')
    setErrors({})
  }

  const submit = async () => {
    if (!request) return
    setErrors({})
    try {
      const note = await create.mutateAsync(request)
      toast.success(`Credit note ${note.number} issued`)
      setSaved(note.id)
    } catch (error) {
      if (error instanceof ApiRequestError && error.body.errors) {
        setErrors(Object.fromEntries(Object.entries(error.body.errors).map(([key, messages]) => [key, messages[0] ?? ''])))
      }
      toast.error(errorMessage(error))
    }
  }

  const lineError = (line: (typeof lines)[number]) => {
    const index = entered.indexOf(line)
    return problem(line) ?? (index >= 0 ? (errors[`lines[${index}].${goods ? 'quantity' : 'amount'}`] ?? errors[`lines[${index}].disposition`]) : undefined)
  }

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <UnsavedChangesGuard when={(entered.length > 0 || reasonNote.trim() !== '') && saved === null} />
      <PageHeader
        title={`Credit note against ${invoice.number}`}
        description={`${invoice.customerName} · invoiced ${formatBusinessDate(invoice.invoiceDate)} · ${formatINR(invoice.grandTotal)}, ${formatINR(invoice.balance)} still due`}
        actions={
          <Button variant="ghost" asChild>
            <Link to={`/invoices/${invoice.id}`}>
              <ArrowLeft />
              Back to invoice
            </Link>
          </Button>
        }
      />

      {afterDeadline && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <Info />
          <AlertDescription className="text-inherit">
            GST on this invoice could be reduced only until {formatBusinessDate(invoice.gstCreditDeadline)} (CGST Act s.34). This credit note credits the value
            only, with no GST, and the GST reports leave it out.
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>What's being credited</CardTitle>
          <CardDescription>Goods the customer sent back, or a correction to the value with no goods moving: a wrong rate, short supply or a discount agreed later.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <Tabs value={kind} onValueChange={(v) => changeKind(v as CreditNoteKind)}>
            <TabsList>
              <TabsTrigger value="GoodsReturn">Goods returned</TabsTrigger>
              <TabsTrigger value="PriceCorrection">Price correction</TabsTrigger>
            </TabsList>
          </Tabs>
          <FieldGroup className="grid gap-4 sm:grid-cols-3">
            <Field>
              <FieldLabel htmlFor="cn-reason">Reason</FieldLabel>
              <Select value={reason} onValueChange={(v) => setReason(v as ReturnReason)}>
                <SelectTrigger id="cn-reason" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(goods ? goodsReasons : priceReasons).map((r) => (
                    <SelectItem key={r} value={r}>
                      {returnReasonLabels[r]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor="cn-note">Details</FieldLabel>
              <Input id="cn-note" value={reasonNote} maxLength={500} placeholder="Optional, e.g. two bales wet in transit" onChange={(e) => setReasonNote(e.target.value)} />
            </Field>
            <Field data-invalid={!!errors.creditNoteDate}>
              <FieldLabel htmlFor="cn-date">Credit note date</FieldLabel>
              <Input id="cn-date" type="date" value={date} min={invoice.invoiceDate} max={todayInIndia()} onChange={(e) => setDate(e.target.value)} />
              <FieldError errors={errors.creditNoteDate ? [{ message: errors.creditNoteDate }] : []} />
            </Field>
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{goods ? 'What came back' : 'Value to take off'}</CardTitle>
          <CardDescription>
            {goods
              ? 'Enter the quantity returned on each line. Goods fit to sell go back into stock; damaged ones are recorded as returned and written off.'
              : 'Enter the amount to take off each line, before GST. GST is worked out on it at the invoice’s rate.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>Product</TableHead>
                  <TableHead className="text-right">Invoiced</TableHead>
                  <TableHead className="text-right">{goods ? 'Returned so far' : 'Credited so far'}</TableHead>
                  <TableHead className="w-40">{goods ? 'Returned now' : 'Amount off (₹)'}</TableHead>
                  {goods && <TableHead className="w-48">Goods</TableHead>}
                  <TableHead className="text-right">Credit</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lines.map((l) => {
                  const error = lineError(l)
                  const previewLine = p?.lines.find((x) => x.invoiceLineId === l.id)
                  const nothingLeft = goods ? l.quantityLeft <= 0 || l.valueLeft <= 0 : l.valueLeft <= 0
                  return (
                    <TableRow key={l.id} className="align-top hover:bg-transparent">
                      <TableCell>
                        <span className="block text-sm">{l.productName}</span>
                        <span className="font-mono text-xs text-muted-foreground">{l.sku}</span>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatQuantity(l.quantity, l.unit)}
                        <span className="block text-xs text-muted-foreground">{formatINR(l.taxableValue)}</span>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{goods ? formatQuantity(l.quantityReturned, l.unit) : formatINR(l.valueCredited)}</TableCell>
                      <TableCell>
                        <Field data-invalid={!!error}>
                          <Input
                            value={(goods ? quantities[l.id] : amounts[l.id]) ?? ''}
                            inputMode="decimal"
                            placeholder={nothingLeft ? '—' : '0'}
                            disabled={nothingLeft}
                            aria-label={`${goods ? 'Quantity returned' : 'Amount off'} for ${l.sku}`}
                            aria-invalid={!!error}
                            onChange={(e) => (goods ? setQuantities : setAmounts)((current) => ({ ...current, [l.id]: e.target.value }))}
                          />
                          <FieldError errors={error ? [{ message: error }] : []} />
                        </Field>
                      </TableCell>
                      {goods && (
                        <TableCell>
                          <Select
                            value={dispositions[l.id] ?? 'Restock'}
                            onValueChange={(v) => setDispositions((current) => ({ ...current, [l.id]: v as ReturnDisposition }))}
                            disabled={nothingLeft}
                          >
                            <SelectTrigger className="w-full" aria-label={`What happens to ${l.sku}`}>
                              <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                              {(['Restock', 'Damaged'] as ReturnDisposition[]).map((d) => (
                                <SelectItem key={d} value={d}>
                                  {dispositionLabels[d]}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </TableCell>
                      )}
                      <TableCell className="text-right tabular-nums">{previewLine ? formatINR(previewLine.lineTotal) : '—'}</TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>

          <div className="mt-4 flex flex-wrap items-start justify-between gap-6">
            <div className="max-w-md text-sm text-muted-foreground">
              {preview.error && request ? (
                <p className="text-destructive">{errorMessage(preview.error)}</p>
              ) : p ? (
                <p>
                  {p.offInvoice > 0 && (
                    <>
                      {formatINR(p.offInvoice)} comes off what {invoice.customerName} owes on {invoice.number}.{' '}
                    </>
                  )}
                  {p.leftAsCredit > 0 && <>{formatINR(p.leftAsCredit)} stays as credit on their account, to set against another invoice or refund.</>}
                </p>
              ) : (
                <p>Enter {goods ? 'a quantity' : 'an amount'} to see the credit note.</p>
              )}
            </div>
            <dl className="grid min-w-72 grid-cols-[1fr_auto] gap-x-8 gap-y-1.5 text-sm tabular-nums">
              <dt className="text-muted-foreground">Taxable value</dt>
              <dd className="text-right">{p ? formatINR(p.taxableValue) : '—'}</dd>
              {invoice.isInterState ? (
                <>
                  <dt className="text-muted-foreground">IGST</dt>
                  <dd className="text-right">{p ? formatINR(p.igstAmount) : '—'}</dd>
                </>
              ) : (
                <>
                  <dt className="text-muted-foreground">CGST</dt>
                  <dd className="text-right">{p ? formatINR(p.cgstAmount) : '—'}</dd>
                  <dt className="text-muted-foreground">{invoice.usesUtgst ? 'UTGST' : 'SGST'}</dt>
                  <dd className="text-right">{p ? formatINR(p.sgstAmount) : '—'}</dd>
                </>
              )}
              <dt className="text-muted-foreground">Round off</dt>
              <dd className="text-right">{p ? formatINR(p.roundOff) : '—'}</dd>
              <dt className="border-t pt-1.5 font-semibold">Credit note total</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{p ? formatINR(p.grandTotal) : '—'}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        <Button type="button" variant="outline" onClick={() => navigate(`/invoices/${invoice.id}`)}>
          Cancel
        </Button>
        <Button type="button" disabled={!request || create.isPending} onClick={submit}>
          {create.isPending && <Spinner />}
          Issue credit note
        </Button>
      </div>
    </div>
  )
}
