import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatBusinessDate, formatINR, todayInIndia } from '@/lib/format'
import { useRates } from './api'

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
          <CardTitle>Rates</CardTitle>
          <CardDescription>
            Today: {formatINR(current?.rate ?? listPrice)} {current ? 'from the rate list' : '(the list price; no rate period covers today)'}.
          </CardDescription>
        </div>
        <Button asChild variant="outline" size="sm">
          <Link to="/rates">Rate updates</Link>
        </Button>
      </CardHeader>
      <CardContent>
        {rates.isPending ? (
          <Skeleton className="h-20 w-full" />
        ) : items.length === 0 ? (
          <p className="text-sm text-muted-foreground">No rate periods yet. Orders use the list price.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Period</TableHead>
                <TableHead className="text-right">Rate</TableHead>
                <TableHead>Note</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <span className="inline-flex items-center gap-2">
                      {r.effectiveTo ? `${formatBusinessDate(r.effectiveFrom)} to ${formatBusinessDate(r.effectiveTo)}` : `${formatBusinessDate(r.effectiveFrom)} onwards`}
                      {r.id === current?.id && <Badge variant="secondary">Today</Badge>}
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
