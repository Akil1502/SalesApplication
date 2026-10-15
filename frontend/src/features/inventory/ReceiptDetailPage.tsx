import { ArrowLeft } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatQuantity } from '@/lib/format'
import { useStockReceipt } from './api'

function Detail({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div className="space-y-1">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="font-medium">{value || '—'}</dd>
    </div>
  )
}

export function ReceiptDetailPage() {
  const id = Number(useParams().id)
  const { can } = useAuth()
  const showCost = can(Permissions.CostPricesView)
  const receipt = useStockReceipt(id)

  if (receipt.isPending) return <Skeleton className="h-80 w-full max-w-4xl" />
  if (receipt.error) {
    return (
      <Alert variant="destructive" className="max-w-xl">
        <AlertDescription>{errorMessage(receipt.error)}</AlertDescription>
      </Alert>
    )
  }

  const r = receipt.data
  const total = r.lines.reduce((sum, l) => sum + (l.unitCost ?? 0) * l.quantity, 0)

  return (
    <div className="flex max-w-4xl flex-col gap-6">
      <PageHeader
        title={`Goods receipt ${r.number}`}
        description={`Recorded by ${r.createdByName} on ${formatDate(r.createdAtUtc)}. Receipts can't be edited; correct mistakes with a stock adjustment.`}
        actions={
          <Button variant="ghost" asChild>
            <Link to="/stock?tab=receipts">
              <ArrowLeft />
              Back to receipts
            </Link>
          </Button>
        }
      />

      <Card>
        <CardContent>
          <dl className="grid gap-4 sm:grid-cols-4">
            <Detail label="Received on" value={formatBusinessDate(r.receiptDate)} />
            <Detail label="Supplier" value={r.supplierName} />
            <Detail label="Supplier ref" value={r.supplierReference} />
            <Detail label="Notes" value={r.notes} />
          </dl>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Products</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-hidden rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>SKU</TableHead>
                  <TableHead>Product</TableHead>
                  <TableHead className="text-right">Quantity</TableHead>
                  {showCost && <TableHead className="text-right">Cost per unit</TableHead>}
                  {showCost && <TableHead className="text-right">Line total</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {r.lines.map((line) => (
                  <TableRow key={line.movementId}>
                    <TableCell className="font-mono text-xs">{line.sku}</TableCell>
                    <TableCell>{line.productName}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatQuantity(line.quantity, line.unit)}</TableCell>
                    {showCost && <TableCell className="text-right tabular-nums">{formatINR(line.unitCost)}</TableCell>}
                    {showCost && (
                      <TableCell className="text-right tabular-nums">{line.unitCost == null ? '—' : formatINR(line.unitCost * line.quantity)}</TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
              {showCost && (
                <TableFooter>
                  <TableRow>
                    <TableCell colSpan={4} className="text-right">
                      Total cost
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{formatINR(total)}</TableCell>
                  </TableRow>
                </TableFooter>
              )}
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
