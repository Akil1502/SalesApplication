import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { errorMessage } from '@/lib/api/errors'
import { formatINR, formatQuantity } from '@/lib/format'
import { useCreateReorderDrafts, useReorderCandidates, type ReorderCandidate } from './api'

interface ReorderDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Exactly these products (the AI insights' suggestions); otherwise everything at or below its reorder level. */
  productIds?: number[]
  /** Quantities to start with, by product id; otherwise each product's suggested quantity. */
  quantities?: Record<number, number>
}

/**
 * Pick products to reorder and turn them into draft purchase orders, one per preferred supplier. The drafts can be
 * checked and changed before they're placed.
 */
export function ReorderDialog({ open, onOpenChange, productIds, quantities }: ReorderDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>Reorder</DialogTitle>
          <DialogDescription>
            {productIds
              ? 'The products the AI insights suggest ordering, at the quantities they suggest.'
              : 'Products at or below their reorder level that no open order covers yet.'}{' '}
            Each supplier gets one draft order, priced at what you last paid them; check it before placing it.
          </DialogDescription>
        </DialogHeader>
        {open && <Candidates productIds={productIds} quantities={quantities} onDone={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  )
}

/** Loads the products to reorder; mounted only while the dialog is open, so each opening starts fresh. */
function Candidates({ productIds, quantities, onDone }: { productIds?: number[]; quantities?: Record<number, number>; onDone: () => void }) {
  const candidates = useReorderCandidates(productIds)
  if (candidates.isPending) return <Skeleton className="h-48 w-full" />
  if (candidates.error) return <p className="text-sm text-destructive">{errorMessage(candidates.error)}</p>
  return <Picker rows={candidates.data} quantities={quantities} onDone={onDone} />
}

function Picker({ rows, quantities, onDone }: { rows: ReorderCandidate[]; quantities?: Record<number, number>; onDone: () => void }) {
  const navigate = useNavigate()
  const create = useCreateReorderDrafts()
  // Every product that has a supplier starts ticked, at its suggested quantity.
  const [picked, setPicked] = useState<Record<number, string>>(() =>
    Object.fromEntries(rows.filter((c) => c.supplierId !== null).map((c) => [c.productId, String(quantities?.[c.productId] ?? c.suggestedQuantity)])),
  )

  const chosen = rows.filter((c) => picked[c.productId] !== undefined)
  const invalid = chosen.some((c) => !(Number(picked[c.productId]) > 0))
  const suppliers = new Set(chosen.map((c) => c.supplierId)).size

  const toggle = (c: ReorderCandidate, on: boolean) =>
    setPicked((current) => {
      const next = { ...current }
      if (on) next[c.productId] = String(quantities?.[c.productId] ?? c.suggestedQuantity)
      else delete next[c.productId]
      return next
    })

  const submit = async () => {
    try {
      const orders = await create.mutateAsync({ lines: chosen.map((c) => ({ productId: c.productId, quantity: Number(picked[c.productId]) })) })
      toast.success(orders.length === 1 ? `Draft order ${orders[0]!.number} created` : `${orders.length} draft orders created`)
      onDone()
      navigate(orders.length === 1 ? `/purchase-orders/${orders[0]!.id}` : '/purchase-orders?view=Draft')
    } catch (error) {
      toast.error(errorMessage(error))
    }
  }

  return (
    <>
      {rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">Nothing needs reordering: every product is above its reorder level or already on order.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border">
          <Table>
            <TableHeader className="bg-muted/50">
              <TableRow>
                <TableHead className="w-10" />
                <TableHead>Product</TableHead>
                <TableHead className="text-right">On hand</TableHead>
                <TableHead className="text-right">On order</TableHead>
                <TableHead>Supplier</TableHead>
                <TableHead className="text-right">Last price</TableHead>
                <TableHead className="w-36">Order</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((c) => {
                const on = picked[c.productId] !== undefined
                return (
                  <TableRow key={c.productId} className="hover:bg-transparent">
                    <TableCell>
                      <Checkbox
                        checked={on}
                        disabled={c.supplierId === null}
                        onCheckedChange={(value) => toggle(c, value === true)}
                        aria-label={`Order ${c.sku}`}
                      />
                    </TableCell>
                    <TableCell>
                      <span className="font-mono text-xs">{c.sku}</span>
                      <span className="block text-sm">{c.name}</span>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatQuantity(c.quantityOnHand, c.unit)}
                      <span className="block text-xs text-muted-foreground">reorder at {formatQuantity(c.reorderLevel)}</span>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{c.onOrder > 0 ? formatQuantity(c.onOrder, c.unit) : '—'}</TableCell>
                    <TableCell>
                      {c.supplierName ?? (
                        <span className="text-sm text-muted-foreground">
                          No preferred supplier.{' '}
                          <Link className="underline underline-offset-4" to={`/products/${c.productId}`} onClick={onDone}>
                            Set one
                          </Link>
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{c.lastPrice != null ? formatINR(c.lastPrice) : '—'}</TableCell>
                    <TableCell>
                      <Input
                        value={picked[c.productId] ?? ''}
                        disabled={!on}
                        inputMode="decimal"
                        aria-label={`Quantity of ${c.sku}`}
                        aria-invalid={on && !(Number(picked[c.productId]) > 0)}
                        onChange={(e) => setPicked((current) => ({ ...current, [c.productId]: e.target.value }))}
                      />
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      )}

      <DialogFooter className="items-center gap-2">
        {chosen.length > 0 && (
          <span className="mr-auto text-sm text-muted-foreground">
            {chosen.length} product{chosen.length === 1 ? '' : 's'} · {suppliers} draft order{suppliers === 1 ? '' : 's'}
          </span>
        )}
        <Button type="button" variant="outline" onClick={onDone}>
          Cancel
        </Button>
        <Button type="button" disabled={chosen.length === 0 || invalid || create.isPending} onClick={submit}>
          {create.isPending && <Spinner />}
          Create draft orders
        </Button>
      </DialogFooter>
    </>
  )
}
