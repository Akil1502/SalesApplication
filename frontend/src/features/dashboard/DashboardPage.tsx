import { Boxes, ClipboardList, Eye, EyeOff, HandCoins, IndianRupee, Maximize2, Minimize2, Plus, RotateCcw, SlidersHorizontal, Wallet } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/PageHeader'
import { SegmentedControl } from '@/components/SegmentedControl'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { useAuth } from '@/features/auth/auth-context'
import { useDashboard, type Dashboard } from '@/features/reports/api'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatINRCompact } from '@/lib/format'
import { useUserSetting } from '@/lib/user-settings'
import { cn } from '@/lib/utils'
import { ChaseCard, NotificationsCard, PurchasesCard, QuickActionsCard, RecentPaymentsCard } from './ExtraCards'
import { change } from './kpi'
import {
  allowedCards,
  CARD_INFO,
  DEFAULT_LAYOUT,
  moveTo,
  normaliseLayout,
  periodInfo,
  PERIODS,
  TILE_TITLES,
  type CardId,
  type CardPlacement,
  type DashboardLayout,
  type DashboardPeriod,
  type TileId,
  type TilePlacement,
} from './layout'
import { RankedBars, type RankedBar } from './RankedBars'
import { SalesHero } from './SalesHero'
import { SalesTrendChart } from './SalesTrendChart'
import { SortableGrid } from './SortableGrid'
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

/** A card with a title, description and an optional link to the full report. */
function ReportCard({ title, description, to, linkText = 'All', children }: { title: string; description: ReactNode; to?: string; linkText?: string; children: ReactNode }) {
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
        {to && (
          <CardAction>
            <Button asChild variant="ghost" size="sm">
              <Link to={to}>{linkText}</Link>
            </Button>
          </CardAction>
        )}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  )
}

// Tailwind needs whole class names, so the tile row's column count is picked from a list.
const TILE_COLUMNS = ['', 'xl:grid-cols-1', 'xl:grid-cols-2', 'xl:grid-cols-3', 'xl:grid-cols-4', 'xl:grid-cols-5']

const NO_LAYOUT = null

export function DashboardPage() {
  const { user, can } = useAuth()
  const [saved, save] = useUserSetting<unknown>('dashboard', NO_LAYOUT)
  const layout = useMemo(() => normaliseLayout(saved), [saved])
  // While customising, changes go to a draft; Done saves it, Cancel drops it.
  const [draft, setDraft] = useState<DashboardLayout | null>(null)
  const editing = draft !== null
  const shown = draft ?? layout

  const dashboard = useDashboard(shown.period)
  const data = dashboard.data
  const kpis = data?.kpis
  const loading = dashboard.isPending
  const period = periodInfo(shown.period)
  const hasSales = data ? data.monthlySales.some((m) => m.invoices > 0) || data.kpis.openOrders > 0 : true

  const updateLayout = (next: (current: DashboardLayout) => DashboardLayout) => (editing ? setDraft(next(shown)) : save(next(layout)))
  const setPeriod = (value: DashboardPeriod) => updateLayout((current) => ({ ...current, period: value }))
  const setTile = (id: TileId, patch: Partial<TilePlacement>) => updateLayout((current) => ({ ...current, tiles: current.tiles.map((t) => (t.id === id ? { ...t, ...patch } : t)) }))
  const setCard = (id: CardId, patch: Partial<CardPlacement>) => updateLayout((current) => ({ ...current, cards: current.cards.map((c) => (c.id === id ? { ...c, ...patch } : c)) }))

  const cards = allowedCards(shown, can)
  const visibleTiles = shown.tiles.filter((t) => !t.hidden)
  const visibleCards = cards.filter((c) => !c.hidden)
  const hiddenTiles = shown.tiles.filter((t) => t.hidden)
  const hiddenCards = cards.filter((c) => c.hidden)

  const description = data
    ? `${period.label} (${formatBusinessDate(data.period.from)} – ${formatBusinessDate(data.period.to)}) · financial year ${data.financialYear} · sales exclude GST`
    : 'Your sales at a glance.'

  const tile = (id: TileId) => {
    switch (id) {
      case 'sales':
        return (
          <StatTile
            label={`Sales ${period.text}`}
            icon={IndianRupee}
            isLoading={loading}
            value={kpis && formatINRCompact(kpis.sales)}
            count={kpis && { value: kpis.sales, format: formatINRCompact }}
            delta={kpis && { value: change(kpis.sales, kpis.salesComparison), comparison: period.comparison }}
            hint={kpis && count(kpis.invoices, 'invoice')}
            to="/reports/sales-by-period"
          />
        )
      case 'collected':
        return (
          <StatTile
            label={`Collected ${period.text}`}
            icon={HandCoins}
            isLoading={loading}
            value={kpis && formatINRCompact(kpis.collected)}
            count={kpis && { value: kpis.collected, format: formatINRCompact }}
            hint={kpis && count(kpis.payments, 'payment')}
            to="/payments"
          />
        )
      case 'receivables':
        return (
          <StatTile
            label="Receivables"
            icon={Wallet}
            isLoading={loading}
            value={kpis && formatINRCompact(kpis.outstanding)}
            count={kpis && { value: kpis.outstanding, format: formatINRCompact }}
            hint={kpis && `${formatINRCompact(kpis.overdue)} overdue on ${count(kpis.overdueInvoices, 'invoice')}`}
            to="/invoices?view=overdue"
          />
        )
      case 'orders':
        return (
          <StatTile
            label="Confirmed orders to invoice"
            icon={ClipboardList}
            isLoading={loading}
            value={kpis && formatINRCompact(kpis.openOrdersValue)}
            count={kpis && { value: kpis.openOrdersValue, format: formatINRCompact }}
            hint={kpis && count(kpis.openOrders, 'order')}
            to="/orders?status=Confirmed"
          />
        )
      case 'stock':
        return (
          <StatTile
            label="Low on stock"
            icon={Boxes}
            isLoading={loading}
            value={kpis && count(kpis.lowStockProducts, 'product')}
            count={kpis && { value: kpis.lowStockProducts, format: (n) => count(Math.round(n), 'product') }}
            hint="At or below reorder level"
            to="/stock?status=Low"
          />
        )
    }
  }

  const card = (id: CardId) => {
    switch (id) {
      case 'trend':
        return (
          <ReportCard title="Monthly sales" description="Invoiced value excluding GST, last 24 months">
            <div className={dashboard.isFetching && !loading ? 'opacity-60 transition-opacity' : undefined}>
              {data ? <SalesTrendChart months={data.monthlySales} /> : <Skeleton className="h-72 w-full" />}
            </div>
          </ReportCard>
        )
      case 'ageing':
        return (
          <ReportCard title="Receivables ageing" description={kpis ? `${formatINRCompact(kpis.outstanding)} outstanding today` : 'Outstanding today'} to="/reports/receivables-ageing" linkText="By customer">
            {data ? <RankedBars items={ageingBars(data)} emptyText="Nothing outstanding." /> : <CardSkeleton />}
          </ReportCard>
        )
      case 'top-customers':
        return (
          <ReportCard title="Top customers" description={`${period.label}, share of sales`} to="/reports/sales-by-customer">
            {data ? <RankedBars items={data.topCustomers} emptyText={`No sales ${period.text} yet.`} /> : <CardSkeleton />}
          </ReportCard>
        )
      case 'top-products':
        return (
          <ReportCard title="Top products" description={`${period.label}, share of sales`} to="/reports/sales-by-product">
            {data ? <RankedBars items={data.topProducts} emptyText={`No sales ${period.text} yet.`} /> : <CardSkeleton />}
          </ReportCard>
        )
      case 'states':
        return (
          <ReportCard title="Sales by state" description={`Place of supply, ${period.text}`}>
            {data ? (
              <RankedBars items={data.salesByState.map((s) => (s.label.startsWith('Other') ? { ...s, color: 'var(--viz-other)' } : s))} emptyText={`No sales ${period.text} yet.`} />
            ) : (
              <CardSkeleton rows={7} />
            )}
          </ReportCard>
        )
      case 'chase':
        return <ChaseCard />
      case 'payments':
        return <RecentPaymentsCard />
      case 'purchases':
        return <PurchasesCard />
      case 'quick-actions':
        return <QuickActionsCard />
      case 'notifications':
        return <NotificationsCard />
    }
  }

  const hideButton = (label: string, onHide: () => void) => (
    <button type="button" onClick={onHide} aria-label={`Hide ${label}`} title="Hide" className="flex size-8 items-center justify-center rounded-full hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
      <EyeOff className="size-4" />
    </button>
  )

  return (
    <>
      {shown.city ? (
        <SalesHero greeting={`${greeting()}, ${user?.fullName ?? ''}`} description={description} months={data?.monthlySales} />
      ) : (
        <PageHeader title={`${greeting()}, ${user?.fullName ?? ''}`} description={description} />
      )}

      {editing ? (
        <div className="sticky top-16 z-40 flex flex-wrap items-center gap-2 rounded-2xl border bg-popover/95 p-2 pl-4 text-popover-foreground shadow-[var(--shadow-lifted)] backdrop-blur" role="region" aria-label="Customise the dashboard">
          <p className="mr-auto min-w-48 flex-1 text-sm">
            <span className="font-semibold">Customising your dashboard.</span>{' '}
            <span className="text-muted-foreground">Drag a card by its handle, or focus the handle and use the arrow keys. Only you see your layout.</span>
          </p>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" disabled={hiddenTiles.length + hiddenCards.length === 0}>
                <Plus /> Add ({hiddenTiles.length + hiddenCards.length})
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-72">
              {hiddenCards.length > 0 && <DropdownMenuLabel>Cards</DropdownMenuLabel>}
              {hiddenCards.map((c) => (
                <DropdownMenuItem
                  key={c.id}
                  onSelect={() => setCard(c.id, { hidden: false })}
                  className="flex-col items-start gap-0.5"
                  aria-label={CARD_INFO[c.id].title}
                  aria-describedby={`add-card-${c.id}`}
                >
                  <span className="flex items-center gap-2 font-medium">
                    <Eye className="size-4" /> {CARD_INFO[c.id].title}
                  </span>
                  <span id={`add-card-${c.id}`} className="pl-6 text-xs text-muted-foreground">
                    {CARD_INFO[c.id].description}
                  </span>
                </DropdownMenuItem>
              ))}
              {hiddenCards.length > 0 && hiddenTiles.length > 0 && <DropdownMenuSeparator />}
              {hiddenTiles.length > 0 && <DropdownMenuLabel>Figures</DropdownMenuLabel>}
              {hiddenTiles.map((t) => (
                <DropdownMenuItem key={t.id} onSelect={() => setTile(t.id, { hidden: false })}>
                  <Eye className="size-4" /> {TILE_TITLES[t.id]}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
          <div className="flex items-center gap-2 px-2">
            <Switch id="dashboard-city" checked={shown.city} onCheckedChange={(city) => updateLayout((current) => ({ ...current, city }))} />
            <Label htmlFor="dashboard-city" className="text-sm font-normal">
              3D city
            </Label>
          </div>
          <Button variant="ghost" size="sm" onClick={() => setDraft({ ...DEFAULT_LAYOUT, period: shown.period })}>
            <RotateCcw /> Reset
          </Button>
          <Button variant="outline" size="sm" onClick={() => setDraft(null)}>
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={() => {
              save(shown)
              setDraft(null)
            }}
          >
            Done
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-3" data-tour="dashboard-controls">
          <div className="-mx-1 max-w-full overflow-x-auto px-1 pb-1">
            <SegmentedControl
              label="Period"
              value={shown.period}
              onValueChange={setPeriod}
              options={PERIODS.map((p) => ({
                value: p.value,
                label: (
                  <>
                    <span className="sm:hidden">{p.short}</span>
                    <span className="hidden sm:inline">{p.label}</span>
                  </>
                ),
              }))}
            />
          </div>
          <Button variant="outline" size="sm" onClick={() => setDraft(layout)}>
            <SlidersHorizontal /> Customise
          </Button>
        </div>
      )}

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

      <div data-dashboard-editing={editing || undefined} className="flex flex-col gap-6">
        {visibleTiles.length > 0 && (
          <SortableGrid
            items={visibleTiles}
            editing={editing}
            titleOf={(id) => TILE_TITLES[id as TileId]}
            onMove={(id, overId) => updateLayout((current) => ({ ...current, tiles: moveTo(current.tiles, id, overId) }))}
            className={cn('fx-stagger grid gap-4 sm:grid-cols-2 lg:grid-cols-3', TILE_COLUMNS[Math.min(visibleTiles.length, 5)])}
            renderItem={(t) => tile(t.id)}
            renderControls={(t) => hideButton(TILE_TITLES[t.id], () => setTile(t.id, { hidden: true }))}
          />
        )}

        {visibleCards.length > 0 && (
          <SortableGrid
            items={visibleCards}
            editing={editing}
            titleOf={(id) => CARD_INFO[id as CardId].title}
            onMove={(id, overId) => updateLayout((current) => ({ ...current, cards: moveTo(current.cards, id, overId) }))}
            className="grid grid-flow-row-dense gap-4 lg:grid-cols-3"
            itemClassName={(c) => (c.size === 'wide' ? 'lg:col-span-2' : undefined)}
            renderItem={(c) => card(c.id)}
            renderControls={(c) => (
              <>
                <button
                  type="button"
                  onClick={() => setCard(c.id, { size: c.size === 'wide' ? 'normal' : 'wide' })}
                  aria-label={c.size === 'wide' ? `Make ${CARD_INFO[c.id].title} narrower` : `Make ${CARD_INFO[c.id].title} wider`}
                  title={c.size === 'wide' ? 'Narrower' : 'Wider'}
                  className="hidden size-8 items-center justify-center rounded-full hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none lg:flex"
                >
                  {c.size === 'wide' ? <Minimize2 className="size-4" /> : <Maximize2 className="size-4" />}
                </button>
                {hideButton(CARD_INFO[c.id].title, () => setCard(c.id, { hidden: true }))}
              </>
            )}
          />
        )}
      </div>
    </>
  )
}
