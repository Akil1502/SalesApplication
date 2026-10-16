import { Boxes, ClipboardList, IndianRupee, TrendingUp, Wallet } from 'lucide-react'
import { Link } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useAuth } from '@/features/auth/auth-context'
import { useDashboard, type Dashboard } from '@/features/reports/api'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatINRCompact, todayInIndia } from '@/lib/format'
import { RankedBars, type RankedBar } from './RankedBars'
import { SalesHero } from './SalesHero'
import { SalesTrendChart } from './SalesTrendChart'
import { change } from './kpi'
import { StatTile } from './StatTile'

function greeting(now = new Date()): string {
  const hour = now.getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 17) return 'Good afternoon'
  return 'Good evening'
}

const count = (n: number, one: string, many = `${one}s`) => `${n.toLocaleString('en-IN')} ${n === 1 ? one : many}`

function ageingBars(dashboard: Dashboard): RankedBar[] {
  const { ageing, kpis } = dashboard
  const share = (value: number) => (kpis.outstanding > 0 ? value / kpis.outstanding : 0)
  return [
    { label: 'Not yet due', value: ageing.notDue, color: 'var(--viz-age-1)' },
    { label: '1–30 days overdue', value: ageing.days1To30, color: 'var(--viz-age-2)' },
    { label: '31–60 days overdue', value: ageing.days31To60, color: 'var(--viz-age-3)' },
    { label: '61–90 days overdue', value: ageing.days61To90, color: 'var(--viz-age-4)' },
    { label: 'Over 90 days overdue', value: ageing.over90, color: 'var(--viz-age-5)' },
  ].map((bar) => ({ ...bar, share: share(bar.value) }))
}

function CardSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-4">
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className="h-9 w-full" />
      ))}
    </div>
  )
}

export function DashboardPage() {
  const { user } = useAuth()
  const dashboard = useDashboard()
  const data = dashboard.data
  const kpis = data?.kpis
  const loading = dashboard.isPending
  const hasSales = data ? data.monthlySales.some((m) => m.invoices > 0) || data.kpis.openOrders > 0 : true

  return (
    <>
      <SalesHero
        greeting={`${greeting()}, ${user?.fullName ?? ''}`}
        description={
          data
            ? `Financial year ${data.financialYear} · sales exclude GST · as of ${formatBusinessDate(todayInIndia())}`
            : 'Your sales at a glance.'
        }
        months={data?.monthlySales}
      />

      {dashboard.error && (
        <Alert variant="destructive">
          <AlertDescription>{errorMessage(dashboard.error)}</AlertDescription>
        </Alert>
      )}

      {!hasSales && (
        <Alert>
          <AlertDescription>
            No sales yet. Figures appear here as soon as orders are confirmed and invoiced. For a realistic demo, a developer can generate two years of
            sample history with <code className="rounded bg-muted px-1">dotnet run --project backend/src/SalesApp.Api -- --seed-demo</code>.
          </AlertDescription>
        </Alert>
      )}

      <div className="fx-stagger grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
        <StatTile
          label="Sales this month"
          icon={IndianRupee}
          isLoading={loading}
          value={kpis && formatINRCompact(kpis.salesMonthToDate)}
          count={kpis && { value: kpis.salesMonthToDate, format: formatINRCompact }}
          delta={kpis && { value: change(kpis.salesMonthToDate, kpis.salesLastMonthSamePeriod), comparison: 'same days last month' }}
          hint={kpis && count(kpis.invoicesMonthToDate, 'invoice')}
          to="/invoices"
        />
        <StatTile
          label="Sales this financial year"
          icon={TrendingUp}
          isLoading={loading}
          value={kpis && formatINRCompact(kpis.salesFinancialYearToDate)}
          count={kpis && { value: kpis.salesFinancialYearToDate, format: formatINRCompact }}
          delta={kpis && { value: change(kpis.salesFinancialYearToDate, kpis.salesPreviousYearSamePeriod), comparison: 'same period last year' }}
          to="/reports/sales-by-period"
        />
        <StatTile
          label="Receivables"
          icon={Wallet}
          isLoading={loading}
          value={kpis && formatINRCompact(kpis.outstanding)}
          count={kpis && { value: kpis.outstanding, format: formatINRCompact }}
          hint={kpis && `${formatINRCompact(kpis.overdue)} overdue on ${count(kpis.overdueInvoices, 'invoice')}`}
          to="/invoices?view=overdue"
        />
        <StatTile
          label="Confirmed orders to invoice"
          icon={ClipboardList}
          isLoading={loading}
          value={kpis && formatINRCompact(kpis.openOrdersValue)}
          count={kpis && { value: kpis.openOrdersValue, format: formatINRCompact }}
          hint={kpis && count(kpis.openOrders, 'order')}
          to="/orders?status=Confirmed"
        />
        <StatTile
          label="Low on stock"
          icon={Boxes}
          isLoading={loading}
          value={kpis && count(kpis.lowStockProducts, 'product')}
          count={kpis && { value: kpis.lowStockProducts, format: (n) => count(Math.round(n), 'product') }}
          hint="At or below reorder level"
          to="/stock?status=Low"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Monthly sales</CardTitle>
            <CardDescription>Invoiced value excluding GST, last 24 months</CardDescription>
          </CardHeader>
          <CardContent className={dashboard.isFetching && !loading ? 'opacity-60 transition-opacity' : undefined}>
            {data ? <SalesTrendChart months={data.monthlySales} /> : <Skeleton className="h-72 w-full" />}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Receivables ageing</CardTitle>
            <CardDescription>{kpis ? `${formatINRCompact(kpis.outstanding)} outstanding today` : 'Outstanding today'}</CardDescription>
            <CardAction>
              <Button asChild variant="ghost" size="sm">
                <Link to="/reports/receivables-ageing">By customer</Link>
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>{data ? <RankedBars items={ageingBars(data)} emptyText="Nothing outstanding." /> : <CardSkeleton />}</CardContent>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>Top customers</CardTitle>
            <CardDescription>This financial year, share of sales</CardDescription>
            <CardAction>
              <Button asChild variant="ghost" size="sm">
                <Link to="/reports/sales-by-customer">All</Link>
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>{data ? <RankedBars items={data.topCustomers} emptyText="No sales this year yet." /> : <CardSkeleton />}</CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Top products</CardTitle>
            <CardDescription>This financial year, share of sales</CardDescription>
            <CardAction>
              <Button asChild variant="ghost" size="sm">
                <Link to="/reports/sales-by-product">All</Link>
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>{data ? <RankedBars items={data.topProducts} emptyText="No sales this year yet." /> : <CardSkeleton />}</CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Sales by state</CardTitle>
            <CardDescription>Place of supply, this financial year</CardDescription>
          </CardHeader>
          <CardContent>
            {data ? (
              <RankedBars
                items={data.salesByState.map((s) => (s.label.startsWith('Other') ? { ...s, color: 'var(--viz-other)' } : s))}
                emptyText="No sales this year yet."
              />
            ) : (
              <CardSkeleton rows={7} />
            )}
          </CardContent>
        </Card>
      </div>
    </>
  )
}
