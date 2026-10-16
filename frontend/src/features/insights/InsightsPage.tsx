import {
  ArrowRight,
  CalendarRange,
  CheckCircle2,
  Clock,
  IndianRupee,
  Info,
  Lightbulb,
  PackageX,
  ReceiptIndianRupee,
  RefreshCw,
  TrendingDown,
  TrendingUp,
  TriangleAlert,
  Undo2,
  UserX,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { StatTile } from '@/features/dashboard/StatTile'
import { ReorderDialog } from '@/features/purchasing/ReorderDialog'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatINRCompact } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useInsights, useRefreshInsights, type Anomaly, type AnomalyKind, type CustomerTrend, type Insights, type InsightCardKind } from './api'
import { ForecastChart } from './ForecastChart'
import { anomalyLabels, anomalyLink, coverText, forecastExplanation, monthYear } from './insight-format'

const cardStyles: Record<InsightCardKind, { icon: LucideIcon; label: string; className: string }> = {
  Opportunity: { icon: TrendingUp, label: 'Opportunity', className: 'text-viz-up' },
  Risk: { icon: TriangleAlert, label: 'Risk', className: 'text-viz-down' },
  Action: { icon: ArrowRight, label: 'Action', className: 'text-primary' },
  Info: { icon: Info, label: 'Info', className: 'text-muted-foreground' },
}

const anomalyIcons: Record<AnomalyKind, LucideIcon> = {
  StoppedBuying: UserX,
  UnusualInvoice: ReceiptIndianRupee,
  DemandSpike: TrendingUp,
  DemandDrop: TrendingDown,
  Stockout: PackageX,
  PaymentRisk: Wallet,
  HighReturns: Undo2,
}

/** Claude's written summary, or why there isn't one. Only parts whose figures were found in the metrics are shown. */
function Briefing({ insights }: { insights: Insights }) {
  const narrative = insights.narrative ?? null
  const note = insights.narrativeNote ?? null

  if (!narrative) {
    return (
      <Card>
        <CardContent className="flex items-start gap-3 text-sm text-muted-foreground">
          <Lightbulb className="mt-0.5 size-4 shrink-0" />
          <p>
            {insights.narrativeStatus === 'AiOff'
              ? 'The written summary needs the AI features, which are switched off. Everything below is computed from your sales data without AI.'
              : `${note ?? "The written summary couldn't be produced this time."} The figures below are unaffected.`}
          </p>
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center gap-2">
          {insights.narrativeStatus === 'Verified' ? (
            <Badge variant="secondary" className="gap-1">
              <CheckCircle2 className="size-3 text-viz-up" /> Every figure checked against the data
            </Badge>
          ) : (
            <Badge variant="secondary" className="gap-1">
              <TriangleAlert className="size-3 text-amber-600" /> Parts with unconfirmed figures were left out
            </Badge>
          )}
          <span className="text-xs text-muted-foreground">Written by Claude from the figures below</span>
        </div>
        <CardTitle className="text-lg leading-snug">{narrative.headline}</CardTitle>
        {narrative.summary && <CardDescription className="text-sm leading-relaxed text-foreground">{narrative.summary}</CardDescription>}
      </CardHeader>
      {narrative.cards.length > 0 && (
        <CardContent className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {narrative.cards.map((card) => {
            const style = cardStyles[card.kind]
            return (
              <div key={card.title} className="flex flex-col gap-1 rounded-lg border p-3">
                <p className={cn('flex items-center gap-1.5 text-xs font-medium', style.className)}>
                  <style.icon className="size-3.5" /> {style.label}
                </p>
                <p className="text-sm font-medium">{card.title}</p>
                <p className="text-sm text-muted-foreground">{card.body}</p>
              </div>
            )
          })}
        </CardContent>
      )}
      {note && <CardContent className="text-xs text-muted-foreground">{note}</CardContent>}
    </Card>
  )
}

function AnomalyItem({ anomaly }: { anomaly: Anomaly }) {
  const Icon = anomalyIcons[anomaly.kind]
  const link = anomalyLink(anomaly)
  return (
    <li className="flex gap-3 py-3 first:pt-0 last:pb-0">
      <Icon className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
      <div className="min-w-0 space-y-0.5 text-sm">
        <p className="text-xs font-medium text-muted-foreground">{anomalyLabels[anomaly.kind]}</p>
        <p className="font-medium">{anomaly.title}</p>
        <p className="text-muted-foreground">{anomaly.detail}</p>
        {link && (
          <Link to={link.to} className="text-xs font-medium underline underline-offset-4">
            {link.label}
          </Link>
        )}
      </div>
    </li>
  )
}

function TrendTable({ rows, empty }: { rows: CustomerTrend[]; empty: string }) {
  if (rows.length === 0) return <p className="py-4 text-sm text-muted-foreground">{empty}</p>
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Customer</TableHead>
          <TableHead className="text-right">This year (₹)</TableHead>
          <TableHead className="text-right">Same period last year (₹)</TableHead>
          <TableHead className="text-right">Change</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.customerId}>
            <TableCell>
              <Link to={`/customers/${row.customerId}`} className="font-medium hover:underline">
                {row.name}
              </Link>
              <span className="block text-xs text-muted-foreground">{row.city}</span>
            </TableCell>
            <TableCell className="text-right tabular-nums">{formatINR(row.thisYear, 0).replace('₹', '')}</TableCell>
            <TableCell className="text-right tabular-nums">{formatINR(row.lastYearSamePeriod, 0).replace('₹', '')}</TableCell>
            <TableCell className={cn('text-right tabular-nums', row.change >= 0 ? 'text-viz-up' : 'text-viz-down')}>
              {row.changePercent != null ? `${row.changePercent > 0 ? '+' : ''}${row.changePercent}%` : 'New'}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

function Section({
  title,
  description,
  children,
  className,
  action,
}: {
  title: string
  description?: string
  children: ReactNode
  className?: string
  action?: ReactNode
}) {
  return (
    <Card className={className}>
      <CardHeader className={action ? 'flex flex-row flex-wrap items-start justify-between gap-4' : undefined}>
        <div className="flex flex-col gap-1.5">
          <CardTitle className="text-base">{title}</CardTitle>
          {description && <CardDescription>{description}</CardDescription>}
        </div>
        {action}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  )
}

export function InsightsPage() {
  const insights = useInsights()
  const refresh = useRefreshInsights()
  const { can } = useAuth()
  const [reorderOpen, setReorderOpen] = useState(false)

  function recompute() {
    refresh.mutate(undefined, {
      onSuccess: () => toast.success('Insights updated.'),
      onError: (error) => toast.error(errorMessage(error)),
    })
  }

  const refreshButton = (
    <Button onClick={recompute} disabled={refresh.isPending} variant="outline">
      {refresh.isPending ? <Spinner /> : <RefreshCw />} {refresh.isPending ? 'Working it out…' : 'Refresh now'}
    </Button>
  )

  const data = insights.data ?? null
  const metrics = data?.metrics

  return (
    <>
      <PageHeader
        title="AI insights"
        description="The sales forecast, customer trends, what to reorder and what's out of the ordinary, computed from your sales data. Updated every morning; the written summary keeps only what its figures support."
        actions={refreshButton}
      />

      {insights.isPending && <Skeleton className="h-96 w-full rounded-xl" />}

      {insights.isError && <p className="text-sm text-destructive">{errorMessage(insights.error)}</p>}

      {insights.isSuccess && !data && (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <Lightbulb />
            </EmptyMedia>
            <EmptyTitle>No insights yet</EmptyTitle>
            <EmptyDescription>They're worked out every morning. Work them out now to see this month's forecast, reorder suggestions and anything unusual.</EmptyDescription>
          </EmptyHeader>
          {refreshButton}
        </Empty>
      )}

      {data && metrics && (
        <>
          <p className="-mt-2 flex items-center gap-1.5 text-xs text-muted-foreground">
            <Clock className="size-3.5" />
            {data.trigger === 'Nightly' ? 'Morning update' : 'Refreshed'} {formatDate(data.generatedAtUtc)}, using data up to {formatBusinessDate(metrics.asOf)}
            {data.costInr > 0 && ` · summary cost about ${formatINR(data.costInr)}`}
          </p>

          <Briefing insights={data} />

          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatTile
              label={`Sales, FY ${metrics.sales.financialYear} so far`}
              icon={IndianRupee}
              value={formatINRCompact(metrics.sales.financialYearToDate)}
              isLoading={false}
              delta={{ value: metrics.sales.growthPercent != null ? metrics.sales.growthPercent / 100 : null, comparison: 'same period last year' }}
            />
            <StatTile
              label={`Sales in ${monthYear(metrics.sales.lastFullMonth)}`}
              icon={CalendarRange}
              value={formatINRCompact(metrics.sales.lastFullMonthSales)}
              isLoading={false}
              delta={{ value: metrics.sales.monthGrowthPercent != null ? metrics.sales.monthGrowthPercent / 100 : null, comparison: 'same month last year' }}
            />
            <StatTile
              label="This month so far"
              icon={TrendingUp}
              value={formatINRCompact(metrics.sales.monthToDate)}
              isLoading={false}
              hint={metrics.forecast.next[0] ? `Forecast for the month: ${formatINRCompact(metrics.forecast.next[0].forecast)}` : undefined}
            />
            <StatTile
              label="Overdue"
              icon={Wallet}
              value={formatINRCompact(metrics.receivables.overdue)}
              isLoading={false}
              hint={`${metrics.receivables.overdueInvoices} invoices from ${metrics.receivables.customersOverdue} customers; ${formatINRCompact(metrics.receivables.overdueOver90Days)} over 90 days`}
              to="/reports/receivables-ageing"
            />
          </div>

          <div className="grid gap-4 xl:grid-cols-5">
            <Section title="Sales forecast" description={forecastExplanation(metrics.forecast)} className="xl:col-span-3">
              <ForecastChart forecast={metrics.forecast} />
            </Section>
            <Section
              title="Worth a look"
              description="Things out of the ordinary in the last year, found by comparing each customer and product with its own history."
              className="xl:col-span-2"
            >
              {metrics.anomalies.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing unusual stands out.</p>
              ) : (
                <ul className="max-h-[28rem] divide-y overflow-y-auto pr-1">
                  {metrics.anomalies.map((anomaly, i) => (
                    <AnomalyItem key={`${anomaly.kind}-${anomaly.customerId ?? anomaly.productId ?? i}-${anomaly.reference ?? ''}`} anomaly={anomaly} />
                  ))}
                </ul>
              )}
            </Section>
          </div>

          <Section
            title="Reorder suggestions"
            description="Products at or below their reorder point: expected demand over the supplier's lead time plus safety stock for about a 95% chance of not running out. Demand counts only days the product was in stock and allows for the season ahead."
            action={
              metrics.reorder.length > 0 &&
              can(Permissions.PurchaseOrdersEdit) && (
                <>
                  <Button variant="outline" size="sm" onClick={() => setReorderOpen(true)}>
                    Draft purchase orders
                  </Button>
                  <ReorderDialog
                    open={reorderOpen}
                    onOpenChange={setReorderOpen}
                    productIds={metrics.reorder.map((r) => r.productId)}
                    quantities={Object.fromEntries(metrics.reorder.map((r) => [r.productId, r.suggestedQuantity]))}
                  />
                </>
              )
            }
          >
            {metrics.reorder.length === 0 ? (
              <p className="text-sm text-muted-foreground">No product needs reordering yet.</p>
            ) : (
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Product</TableHead>
                      <TableHead className="text-right">On hand</TableHead>
                      <TableHead className="text-right">Sells per day</TableHead>
                      <TableHead className="text-right">Stock lasts</TableHead>
                      <TableHead className="text-right">Lead time</TableHead>
                      <TableHead className="text-right">Reorder point</TableHead>
                      <TableHead className="text-right">Order</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {metrics.reorder.map((r) => (
                      <TableRow key={r.productId}>
                        <TableCell>
                          <Link to={`/products/${r.productId}`} className="font-medium hover:underline">
                            {r.sku}
                          </Link>
                          <span className="block text-xs text-muted-foreground">{r.name}</span>
                          {r.runsOutBeforeDelivery && (
                            <Badge variant="destructive" className="mt-1">
                              Runs out before a delivery could arrive
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">{r.onHand.toLocaleString('en-IN')} {r.unit}</TableCell>
                        <TableCell className="text-right tabular-nums">{r.dailyDemand.toLocaleString('en-IN')}</TableCell>
                        <TableCell className="text-right tabular-nums">{coverText(r.daysOfCover)}</TableCell>
                        <TableCell className="text-right tabular-nums">{r.leadTimeDays} days</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {r.reorderPoint.toLocaleString('en-IN')}
                          <span className="block text-xs text-muted-foreground">set at {r.configuredReorderLevel.toLocaleString('en-IN')}</span>
                        </TableCell>
                        <TableCell className="text-right font-medium tabular-nums">
                          {r.suggestedQuantity.toLocaleString('en-IN')} {r.unit}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            )}
          </Section>

          <Section title="Customers" description="This financial year so far against the same period last year (sales excluding GST).">
            <Tabs defaultValue="top">
              <TabsList>
                <TabsTrigger value="top">Top customers</TabsTrigger>
                <TabsTrigger value="growing">Growing</TabsTrigger>
                <TabsTrigger value="declining">Declining</TabsTrigger>
              </TabsList>
              <TabsContent value="top" className="overflow-x-auto">
                <TrendTable rows={metrics.topCustomers} empty="No sales this year yet." />
              </TabsContent>
              <TabsContent value="growing" className="overflow-x-auto">
                <TrendTable rows={metrics.growing} empty="No customer is ahead of last year yet." />
              </TabsContent>
              <TabsContent value="declining" className="overflow-x-auto">
                <TrendTable rows={metrics.declining} empty="No customer is behind last year." />
              </TabsContent>
            </Tabs>
          </Section>
        </>
      )}
    </>
  )
}
