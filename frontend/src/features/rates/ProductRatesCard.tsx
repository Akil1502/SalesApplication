import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatBusinessDate, formatINR, todayInIndia } from '@/lib/format'
import { useRates } from './api'
import { t } from '@/lib/i18n'

/** A product's rate periods, newest first, with the one in force today marked. For users who can see rate updates. */
export function ProductRatesCard({ productId, listPrice }: { productId: number; listPrice: number }) {
  const rates = useRates({ productId, page: 1, pageSize: 12 })
  const today = todayInIndia()
  const items = rates.data?.items ?? []
  const current = items.find((r) => r.effectiveFrom <= today && (!r.effectiveTo || r.effectiveTo >= today))

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4">
        <div className="flex flex-col gap-1.5">
          <CardTitle>{t('Rates')}</CardTitle>
          <CardDescription>
            {current
              ? t('Today: {price} from the rate list.', { price: formatINR(current.rate) })
              : t('Today: {price} (the list price; no rate period covers today).', { price: formatINR(listPrice) })}
          </CardDescription>
        </div>
        <Button asChild variant="outline" size="sm">
          <Link to="/rates">{t('Rate updates')}</Link>
        </Button>
      </CardHeader>
      <CardContent>
        {rates.isPending ? (
          <Skeleton className="h-20 w-full" />
        ) : items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('No rate periods yet. Orders use the list price.')}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t('Period')}</TableHead>
                <TableHead className="text-right">{t('Rate')}</TableHead>
                <TableHead>{t('Note')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <span className="inline-flex items-center gap-2">
                      {r.effectiveTo
                        ? t('{from} to {to}', { from: formatBusinessDate(r.effectiveFrom), to: formatBusinessDate(r.effectiveTo) })
                        : t('{from} onwards', { from: formatBusinessDate(r.effectiveFrom) })}
                      {r.id === current?.id && <Badge variant="secondary">{t('Today')}</Badge>}
                    </span>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatINR(r.rate)}</TableCell>
                  <TableCell className="whitespace-normal text-muted-foreground">{r.note ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}
