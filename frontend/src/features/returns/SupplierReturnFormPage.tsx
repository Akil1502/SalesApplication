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
import { useSupplierBill, type SupplierBill } from '@/features/purchasing/api'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatINR, formatQuantity, todayInIndia } from '@/lib/format'
import { useDebounced } from '@/lib/use-debounced'
import { useCreateSupplierReturn, useSupplierReturnPreview, type ReturnReason, type SupplierReturnCreateRequest } from './api'
import { goodsReasons, returnReasonLabels } from './labels'
import { gstDeadline } from './rules'

/** Sends goods back to a supplier against their bill: our debit note. */
export function SupplierReturnFormPage() {
  const bill = useSupplierBill(Number(useParams().id))

  if (bill.isPending) return <Skeleton className="h-96 w-full max-w-6xl" />
  if (bill.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(bill.error)}</AlertDescription>
      </Alert>
    )
  }

  if (bill.data.status === 'Cancelled') {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{bill.data.number} is cancelled, so nothing can be returned against it.</AlertDescription>
      </Alert>
    )
  }

  return <ReturnForm bill={bill.data} />
}

function ReturnForm({ bill }: { bill: SupplierBill }) {
  const navigate = useNavigate()
  const create = useCreateSupplierReturn()
  const [reason, setReason] = useState<ReturnReason>('QualityIssue')
  const [reasonNote, setReasonNote] = useState('')
  const [date, setDate] = useState(todayInIndia())
  const [theirNumber, setTheirNumber] = useState('')
  const [quantities, setQuantities] = useState<Record<number, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState<number | null>(null)

  useEffect(() => {
    if (saved !== null) navigate(`/supplier-returns/${saved}`)
  }, [saved, navigate])

  const lines = bill.lines.map((l) => ({ ...l, quantityLeft: l.quantity - l.quantityReturned }))
  const raw = (lineId: number) => (quantities[lineId] ?? '').trim()
  const problem = (line: (typeof lines)[number]): string | null => {
    const value = raw(line.id)
    if (value === '') return null
    if (!/^\d+(\.\d{1,3})?$/.test(value)) return 'Up to 3 decimals.'
    if (Number(value) > line.quantityLeft) return line.quantityLeft > 0 ? `At most ${formatQuantity(line.quantityLeft, line.unit)}.` : 'All returned already.'
    return null
  }

  const entered = lines.filter((l) => Number(raw(l.id)) > 0)
  const invalid = lines.some((l) => problem(l) !== null)
  const request: SupplierReturnCreateRequest | null =
    entered.length === 0 || invalid
      ? null
      : {
          supplierBillId: bill.id,
          returnDate: date,
          reason,
          reasonNote: reasonNote.trim() || null,
          supplierCreditNoteNumber: theirNumber.trim() || null,
          supplierCreditNoteDate: null,
          lines: entered.map((l) => ({ supplierBillLineId: l.id, quantity: Number(raw(l.id)) })),
        }

  const requestKey = useDebounced(JSON.stringify(request), 350)
  const previewRequest = useMemo(() => JSON.parse(requestKey) as SupplierReturnCreateRequest | null, [requestKey])
  const preview = useSupplierReturnPreview(previewRequest)
  const p = request ? preview.data : undefined
  const deadline = gstDeadline(bill.billDate)

  const submit = async () => {
    if (!request) return
    setErrors({})
    try {
      const ret = await create.mutateAsync(request)
      toast.success(`Debit note ${ret.number} recorded; stock updated`)
      setSaved(ret.id)
    } catch (error) {
      if (error instanceof ApiRequestError && error.body.errors) {
        setErrors(Object.fromEntries(Object.entries(error.body.errors).map(([key, messages]) => [key, messages[0] ?? ''])))
      }
      toast.error(errorMessage(error))
    }
  }

  return (
    <div className="flex max-w-6xl flex-col gap-6">
      <UnsavedChangesGuard when={entered.length > 0 && saved === null} />
      <PageHeader
        title={`Return goods against ${bill.number}`}
        description={`${bill.supplierName}'s invoice ${bill.supplierInvoiceNumber} of ${formatBusinessDate(bill.billDate)} · ${formatINR(bill.grandTotal)}, ${formatINR(bill.balance)} still to pay`}
        actions={
          <Button variant="ghost" asChild>
            <Link to={`/supplier-bills/${bill.id}`}>
              <ArrowLeft />
              Back to bill
            </Link>
          </Button>
        }
      />

      {date > deadline && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <Info />
          <AlertDescription className="text-inherit">
            Input GST on this bill could be reversed only until {formatBusinessDate(deadline)}. This return reduces the value only, with no GST.
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Why they're going back</CardTitle>
          <CardDescription>
            The goods leave stock at the bill's price, the input GST on them is reversed, and the value comes off what we owe {bill.supplierName}.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <FieldGroup className="grid gap-4 sm:grid-cols-4">
            <Field>
              <FieldLabel htmlFor="dn-reason">Reason</FieldLabel>
              <Select value={reason} onValueChange={(v) => setReason(v as ReturnReason)}>
                <SelectTrigger id="dn-reason" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {goodsReasons.map((r) => (
                    <SelectItem key={r} value={r}>
                      {returnReasonLabels[r]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor="dn-note">Details</FieldLabel>
              <Input id="dn-note" value={reasonNote} maxLength={500} placeholder="Optional, e.g. shade not as sample" onChange={(e) => setReasonNote(e.target.value)} />
            </Field>
            <Field data-invalid={!!errors.returnDate}>
              <FieldLabel htmlFor="dn-date">Returned on</FieldLabel>
              <Input id="dn-date" type="date" value={date} min={bill.billDate} max={todayInIndia()} onChange={(e) => setDate(e.target.value)} />
              <FieldError errors={errors.returnDate ? [{ message: errors.returnDate }] : []} />
            </Field>
            <Field>
              <FieldLabel htmlFor="dn-theirs">Their credit note no.</FieldLabel>
              <Input id="dn-theirs" value={theirNumber} maxLength={30} placeholder="If it has arrived" onChange={(e) => setTheirNumber(e.target.value)} />
            </Field>
          </FieldGroup>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>What's going back</CardTitle>
          <CardDescription>No more than was billed, less what's gone back already, and no more than is in stock.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-lg border">
            <Table stack>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>Product</TableHead>
                  <TableHead className="text-right">Billed</TableHead>
                  <TableHead className="text-right">Rate</TableHead>
                  <TableHead className="text-right">Returned so far</TableHead>
                  <TableHead className="w-40">Returning now</TableHead>
                  <TableHead className="text-right">Value</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lines.map((l) => {
                  const index = entered.indexOf(l)
                  const error = problem(l) ?? (index >= 0 ? errors[`lines[${index}].quantity`] : undefined)
                  const previewLine = p?.lines.find((x) => x.supplierBillLineId === l.id)
                  return (
                    <TableRow key={l.id} className="align-top hover:bg-transparent">
                      <TableCell>
                        <span className="block text-sm">{l.productName}</span>
                        <span className="font-mono text-xs text-muted-foreground">{l.sku}</span>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{formatQuantity(l.quantity, l.unit)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(l.unitPrice)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatQuantity(l.quantityReturned, l.unit)}</TableCell>
                      <TableCell>
                        <Field data-invalid={!!error}>
                          <Input
                            value={quantities[l.id] ?? ''}
                            inputMode="decimal"
                            placeholder={l.quantityLeft > 0 ? '0' : '—'}
                            disabled={l.quantityLeft <= 0}
                            aria-label={`Quantity of ${l.sku} going back`}
                            aria-invalid={!!error}
                            onChange={(e) => setQuantities((current) => ({ ...current, [l.id]: e.target.value }))}
                          />
                          <FieldError errors={error ? [{ message: error }] : []} />
                        </Field>
                      </TableCell>
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
                  {p.offBill > 0 && <>{formatINR(p.offBill)} comes off what we owe on {bill.number}. </>}
                  {p.leftAsCredit > 0 && <>{formatINR(p.leftAsCredit)} is credit with {bill.supplierName}, for their next bill or a refund.</>}
                </p>
              ) : (
                <p>Enter a quantity to see the debit note.</p>
              )}
            </div>
            <dl className="grid min-w-72 grid-cols-[1fr_auto] gap-x-8 gap-y-1.5 text-sm tabular-nums">
              <dt className="text-muted-foreground">Taxable value</dt>
              <dd className="text-right">{p ? formatINR(p.taxableValue) : '—'}</dd>
              {bill.isInterState ? (
                <>
                  <dt className="text-muted-foreground">IGST reversed</dt>
                  <dd className="text-right">{p ? formatINR(p.igstAmount) : '—'}</dd>
                </>
              ) : (
                <>
                  <dt className="text-muted-foreground">CGST reversed</dt>
                  <dd className="text-right">{p ? formatINR(p.cgstAmount) : '—'}</dd>
                  <dt className="text-muted-foreground">SGST reversed</dt>
                  <dd className="text-right">{p ? formatINR(p.sgstAmount) : '—'}</dd>
                </>
              )}
              <dt className="text-muted-foreground">Round off</dt>
              <dd className="text-right">{p ? formatINR(p.roundOff) : '—'}</dd>
              <dt className="border-t pt-1.5 font-semibold">Debit note total</dt>
              <dd className="border-t pt-1.5 text-right font-semibold">{p ? formatINR(p.grandTotal) : '—'}</dd>
            </dl>
          </div>
        </CardContent>
      </Card>

      <div className="sticky bottom-0 -mx-4 flex justify-end gap-2 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        <Button type="button" variant="outline" onClick={() => navigate(`/supplier-bills/${bill.id}`)}>
          Cancel
        </Button>
        <Button type="button" disabled={!request || create.isPending} onClick={submit}>
          {create.isPending && <Spinner />}
          Return goods
        </Button>
      </div>
    </div>
  )
}
