import { CheckCircle2, Plus, RotateCcw, Sparkles, Trash2, TriangleAlert } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { CustomerPicker, type PickedCustomer } from '@/components/CustomerPicker'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ProductPicker } from '@/components/ProductPicker'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api/client'
import { errorMessage, unwrap } from '@/lib/api/errors'
import { formatINR, formatQuantity } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useConfirmSmartOrder, type CustomerMatchSource, type OrderExtraction } from './api'
import { blankLine, buildConfirmRequest, initialNotes, lineProblem, needsAttention, priceDiffers, toReviewLine, type ReviewLine } from './review'

const matchLabels: Record<CustomerMatchSource, string> = {
  None: '',
  Selected: 'Chosen by you',
  Gstin: 'Matched by GSTIN',
  Phone: 'Matched by phone',
  Name: 'Matched by name',
}

const variance = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 1, signDisplay: 'exceptZero' })

function UsageNote({ usage }: { usage: OrderExtraction['usage'] }) {
  const cached = usage.cacheReadTokens > 0 ? ` · ${usage.cacheReadTokens.toLocaleString('en-IN')} tokens reused from cache` : ''
  return (
    <p className="text-xs text-muted-foreground">
      Read in {(usage.durationMs / 1000).toFixed(1)} s · estimated cost {formatINR(usage.costInr)}
      {cached}
    </p>
  )
}

interface ReviewOrderProps {
  extraction: OrderExtraction
  initialCustomer: PickedCustomer | null
  onStartOver: () => void
}

/** The draft as read, for the user to correct and confirm. Nothing is saved until "Create draft order". */
export function ReviewOrder({ extraction, initialCustomer, onStartOver }: ReviewOrderProps) {
  const navigate = useNavigate()
  const confirm = useConfirmSmartOrder()
  const [customer, setCustomer] = useState<PickedCustomer | null>(initialCustomer)
  const [customerTouched, setCustomerTouched] = useState(false)
  const [reference, setReference] = useState(extraction.customerReference ?? '')
  const [notes, setNotes] = useState(() => initialNotes(extraction))
  const [lines, setLines] = useState<ReviewLine[]>(() => extraction.lines.map(toReviewLine))
  const [unmatched, setUnmatched] = useState(extraction.unmatched)
  const [showProblems, setShowProblems] = useState(false)
  const [savedOrderId, setSavedOrderId] = useState<number | null>(null)

  const problems = lines.map(lineProblem)
  const toCheck = lines.filter(needsAttention).length
  const canSave = customer !== null && lines.length > 0 && problems.every((p) => p === null)
  const clues = extraction.customer.clues
  // Shown while the customer chosen before reading is still selected; switching (or confirming the choice) clears it.
  const mismatch = extraction.customer.warning && customer?.id === extraction.customer.customerId && !customerTouched ? extraction.customer : null
  const clueText = [clues.name, clues.city, clues.phone, clues.gstin && `GSTIN ${clues.gstin}`, clues.contactPerson].filter(Boolean).join(' · ')

  const update = (key: string, change: Partial<ReviewLine>) => setLines((current) => current.map((l) => (l.key === key ? { ...l, ...change } : l)))

  async function pickCandidate(id: number) {
    try {
      const c = await unwrap(api.GET('/api/customers/{id}', { params: { path: { id } } }))
      setCustomer({ id: c.id, code: c.code, name: c.name, gstin: c.gstin, city: c.billingAddress.city, stateName: c.billingAddress.stateName })
      setCustomerTouched(true)
    } catch (error) {
      toast.error(errorMessage(error))
    }
  }

  async function save() {
    setShowProblems(true)
    if (!canSave || !customer) return
    try {
      const order = await confirm.mutateAsync(
        buildConfirmRequest({ customerId: customer.id, customerReference: reference, notes, lines, source: extraction.source }),
      )
      toast.success(`Draft order ${order.number} created`)
      setSavedOrderId(order.id)
    } catch (error) {
      toast.error(errorMessage(error))
    }
  }

  // Navigate after the guard has re-rendered as "saved", so leaving doesn't ask about unsaved changes.
  useEffect(() => {
    if (savedOrderId !== null) void navigate(`/orders/${savedOrderId}`)
  }, [savedOrderId, navigate])

  return (
    <>
      <UnsavedChangesGuard when={savedOrderId === null} />

      {!extraction.looksLikeOrder && (
        <Alert>
          <TriangleAlert />
          <AlertDescription>
            This doesn't look like an order, so no lines were read. Add lines yourself, or start over with different text.
          </AlertDescription>
        </Alert>
      )}

      {/* Side by side only on very wide screens; on laptops the lines table needs the full width. */}
      <div className="grid items-start gap-4 2xl:grid-cols-3">
        <Card className="2xl:col-span-2">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Sparkles className="size-4 text-muted-foreground" aria-hidden /> Lines read from the order
            </CardTitle>
            <CardDescription>
              {lines.length === 0
                ? 'No lines yet.'
                : toCheck > 0
                  ? `${lines.length} lines · ${toCheck} highlighted for a check. Prices come from your price list.`
                  : `${lines.length} lines, all clear. Prices come from your price list.`}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {lines.length > 0 && (
              <div className="rounded-lg border">
                <Table>
                  <TableHeader className="bg-muted/50">
                    <TableRow>
                      <TableHead className="min-w-52">In the order</TableHead>
                      <TableHead>Product</TableHead>
                      <TableHead className="w-36">Quantity</TableHead>
                      <TableHead className="text-right">Price</TableHead>
                      <TableHead className="w-10">
                        <span className="sr-only">Remove</span>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {lines.map((line, index) => {
                      const problem = showProblems ? problems[index] : null
                      const attention = needsAttention(line)
                      return (
                        <TableRow key={line.key} className={cn('align-top', attention && 'bg-amber-50/60 dark:bg-amber-950/30')}>
                          <TableCell className="whitespace-normal">
                            {line.sourceText ? (
                              <p className="text-sm italic">“{line.sourceText}”</p>
                            ) : (
                              <p className="text-sm text-muted-foreground">Added by you</p>
                            )}
                            <div className="mt-1 flex flex-wrap gap-1">
                              {line.matchedBy === 'Alias' && (
                                <Badge variant="outline" className="text-xs">
                                  Learned wording
                                </Badge>
                              )}
                              {attention && line.sourceText && (
                                <Badge variant="outline" className="border-amber-600/30 bg-amber-50 text-xs text-amber-800 dark:bg-amber-950 dark:text-amber-300">
                                  <TriangleAlert /> Check
                                </Badge>
                              )}
                            </div>
                            {line.note && <p className="mt-1 text-xs text-muted-foreground">{line.note}</p>}
                          </TableCell>
                          <TableCell className="whitespace-normal">
                            <ProductPicker
                              value={line.product}
                              onChange={(product) => update(line.key, { product })}
                              invalid={problem === 'Choose a product'}
                            />
                            {line.product && (
                              <p className="mt-1 text-xs text-muted-foreground">{formatQuantity(line.product.quantityOnHand, line.product.unit)} in stock</p>
                            )}
                            {problem === 'Choose a product' && <p className="mt-1 text-xs text-destructive">{problem}</p>}
                          </TableCell>
                          <TableCell className="whitespace-normal">
                            <div className="flex items-center gap-2">
                              <Input
                                value={line.quantity}
                                onChange={(e) => update(line.key, { quantity: e.target.value })}
                                inputMode="decimal"
                                aria-label="Quantity"
                                aria-invalid={problem === 'Enter a quantity'}
                                className="w-24 text-right tabular-nums"
                              />
                              <span className="text-xs text-muted-foreground">{line.product?.unit}</span>
                            </div>
                            {line.statedUnit && <p className="mt-1 text-xs text-muted-foreground">Order said: {line.statedUnit}</p>}
                            {problem === 'Enter a quantity' && <p className="mt-1 text-xs text-destructive">{problem}</p>}
                          </TableCell>
                          <TableCell className="text-right whitespace-normal tabular-nums">
                            {line.product ? formatINR(line.product.listPrice) : '—'}
                            {line.statedPrice !== null && (
                              <p className={cn('mt-1 text-xs', priceDiffers(line) ? 'text-amber-700 dark:text-amber-400' : 'text-muted-foreground')}>
                                Order: {formatINR(line.statedPrice)}
                                {priceDiffers(line) && ` (${variance.format(line.priceVariance!)})`}
                              </p>
                            )}
                          </TableCell>
                          <TableCell>
                            <Button
                              variant="ghost"
                              size="icon"
                              aria-label="Remove line"
                              onClick={() => setLines((current) => current.filter((l) => l.key !== line.key))}
                            >
                              <Trash2 />
                            </Button>
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              </div>
            )}

            <div>
              <Button variant="outline" size="sm" onClick={() => setLines((current) => [...current, blankLine()])}>
                <Plus /> Add line
              </Button>
            </div>

            {unmatched.length > 0 && (
              <div className="rounded-lg border border-dashed p-3">
                <p className="text-sm font-medium">Couldn't be read</p>
                <ul className="mt-2 flex flex-col gap-2">
                  {unmatched.map((fragment) => (
                    <li key={fragment} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                      <span className="italic">“{fragment}”</span>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          setLines((current) => [...current, blankLine(fragment)])
                          setUnmatched((current) => current.filter((f) => f !== fragment))
                        }}
                      >
                        <Plus /> Add as a line
                      </Button>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <UsageNote usage={extraction.usage} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Customer and details</CardTitle>
            {clueText && <CardDescription>The order says: {clueText}</CardDescription>}
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="review-customer">Customer</Label>
              <CustomerPicker
                id="review-customer"
                value={customer}
                onChange={(picked) => {
                  setCustomer(picked)
                  setCustomerTouched(true)
                }}
                invalid={showProblems && !customer}
              />
              {customer && !customerTouched && !mismatch && extraction.customer.matchedBy !== 'None' && (
                <p className="flex items-center gap-1 text-xs text-muted-foreground">
                  <CheckCircle2 className="size-3.5" aria-hidden /> {matchLabels[extraction.customer.matchedBy]}
                </p>
              )}
              {showProblems && !customer && <p className="text-xs text-destructive">Choose the customer.</p>}
              {mismatch && (
                <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
                  <TriangleAlert />
                  <AlertDescription className="flex flex-col gap-2 text-inherit">
                    <span>{mismatch.warning}</span>
                    <span className="flex flex-wrap gap-2">
                      {mismatch.suggested && (
                        <Button size="sm" variant="outline" onClick={() => void pickCandidate(mismatch.suggested!.id)}>
                          Switch to {mismatch.suggested.name}
                        </Button>
                      )}
                      <Button size="sm" variant="ghost" onClick={() => setCustomerTouched(true)}>
                        Keep {customer?.name}
                      </Button>
                    </span>
                  </AlertDescription>
                </Alert>
              )}
              {!customer && extraction.customer.candidates.length > 0 && (
                <div className="flex flex-col gap-1">
                  <p className="text-xs text-muted-foreground">Did you mean:</p>
                  {extraction.customer.candidates.map((c) => (
                    <Button key={c.id} variant="outline" size="sm" className="justify-start" onClick={() => void pickCandidate(c.id)}>
                      {c.name} <span className="text-muted-foreground">· {c.city}</span>
                    </Button>
                  ))}
                </div>
              )}
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="review-reference">Customer's PO / reference</Label>
              <Input id="review-reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={50} />
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="review-notes">Notes</Label>
              <Textarea id="review-notes" rows={4} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} />
            </div>
          </CardContent>
          <CardFooter className="flex flex-col items-stretch gap-2">
            <Button onClick={() => void save()} disabled={confirm.isPending || lines.length === 0}>
              {confirm.isPending && <Spinner />} Create draft order
            </Button>
            {showProblems && !canSave && lines.length > 0 && (
              <p className="text-center text-xs text-destructive">Fix the highlighted fields first.</p>
            )}
            <Button variant="ghost" onClick={onStartOver} disabled={confirm.isPending}>
              <RotateCcw /> Start over
            </Button>
          </CardFooter>
        </Card>
      </div>
    </>
  )
}
