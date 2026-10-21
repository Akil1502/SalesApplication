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
import { t } from '@/lib/i18n'
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
  const anyRejected = r.lines.some((l) => l.quantityRejected > 0)

  return (
    <div className="flex max-w-4xl flex-col gap-6">
      <PageHeader
        title={t('Goods receipt {number}', { number: r.number })}
        description={t("Recorded by {name} on {date}. Receipts can't be edited; correct mistakes with a stock adjustment.", { name: r.createdByName, date: formatDate(r.createdAtUtc) })}
        actions={
          <Button variant="ghost" asChild>
            <Link to="/stock?tab=receipts">
              <ArrowLeft />
              {t('Back to receipts')}
            </Link>
          </Button>
        }
      />

      <Card>
        <CardContent>
          <dl className="grid gap-4 sm:grid-cols-5">
            <Detail label={t('Received on')} value={formatBusinessDate(r.receiptDate)} />
            <div className="space-y-1">
              <dt className="text-sm text-muted-foreground">{t('Purchase order')}</dt>
              <dd className="font-medium">
                {r.purchaseOrderId ? (
                  <Link to={`/purchase-orders/${r.purchaseOrderId}`} className="font-mono underline underline-offset-4">
                    {r.purchaseOrderNumber}
                  </Link>
                ) : (
                  t('— (before purchase orders)')
                )}
              </dd>
            </div>
            <Detail label={t('Supplier')} value={r.supplierName} />
            <Detail label={t('Their challan / invoice')} value={r.supplierReference} />
            <Detail label={t('Notes')} value={r.notes} />
          </dl>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('Products')}</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-hidden rounded-lg border">
            <Table stack>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>SKU</TableHead>
                  <TableHead>{t('Product')}</TableHead>
                  <TableHead className="text-right">{anyRejected ? t('Accepted') : t('Quantity')}</TableHead>
                  {anyRejected && <TableHead>{t('Rejected at the door')}</TableHead>}
                  {showCost && <TableHead className="text-right">{t('Cost per unit')}</TableHead>}
                  {showCost && <TableHead className="text-right">{t('Line total')}</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {r.lines.map((line) => (
                  <TableRow key={line.id}>
                    <TableCell className="font-mono text-xs">{line.sku}</TableCell>
                    <TableCell>{line.productName}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatQuantity(line.quantity, line.unit)}</TableCell>
                    {anyRejected && (
                      <TableCell>
                        {line.quantityRejected > 0 ? (
                          <>
                            <span className="tabular-nums">{formatQuantity(line.quantityRejected, line.unit)}</span>
                            <span className="block text-xs text-muted-foreground">{line.rejectionReason}</span>
                          </>
                        ) : (
                          '—'
                        )}
                      </TableCell>
                    )}
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
                    <TableCell colSpan={anyRejected ? 5 : 4} className="text-right">
                      {t('Total cost')}
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
