import { Boxes, PackagePlus, SlidersHorizontal, TriangleAlert, Wallet, X } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import type { PickedProduct } from '@/components/ProductPicker'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { formatBusinessDate, formatDate, formatINR, formatQuantity } from '@/lib/format'
import { useCategories } from '@/lib/lookups'
import {
  movementTypeLabels,
  reasonLabel,
  useStockLevels,
  useStockMovements,
  useStockReceipts,
  useStockSummary,
  type StockLevel,
  type StockMovement,
  type StockMovementType,
  type StockReceiptListItem,
  type StockStatus,
} from './api'
import { AdjustStockDialog } from './AdjustStockDialog'
import { MovementTypeBadge, SignedQuantity, StockStatusBadge } from './StockBadges'

const ALL = 'all'
type Tab = 'levels' | 'movements' | 'receipts'

function SummaryCards() {
  const summary = useStockSummary()
  const cards = [
    { label: 'Active products', value: summary.data?.activeProducts.toLocaleString('en-IN'), icon: Boxes },
    { label: 'Low stock', value: summary.data?.lowStock.toLocaleString('en-IN'), icon: TriangleAlert, hint: 'At or below reorder level' },
    { label: 'Out of stock', value: summary.data?.outOfStock.toLocaleString('en-IN'), icon: TriangleAlert },
    ...(summary.data?.totalStockValue != null
      ? [{ label: 'Stock value at cost', value: formatINR(summary.data.totalStockValue, 0), icon: Wallet, hint: 'Products with a cost price' }]
      : []),
  ]

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {cards.map((card) => (
        <Card key={card.label} className="gap-2 py-4">
          <CardHeader className="flex flex-row items-center justify-between px-4">
            <CardDescription>{card.label}</CardDescription>
            <card.icon className="size-4 text-muted-foreground" />
          </CardHeader>
          <CardContent className="px-4">
            {summary.isPending ? <Skeleton className="h-7 w-20" /> : <p className="text-2xl font-semibold tabular-nums">{card.value}</p>}
            {card.hint && <p className="text-xs text-muted-foreground">{card.hint}</p>}
          </CardContent>
        </Card>
      ))}
    </div>
  )
}

function LevelsTab({ canEdit, showCost, onAdjust }: { canEdit: boolean; showCost: boolean; onAdjust: (product: PickedProduct) => void }) {
  const navigate = useNavigate()
  const [params, update] = useListParams({ sort: 'name' })
  const categories = useCategories()
  const status = params.filters.status as StockStatus | undefined
  const categoryId = params.filters.categoryId ? Number(params.filters.categoryId) : undefined

  const levels = useStockLevels({ q: params.q || undefined, page: params.page, pageSize: params.pageSize, sort: params.sort, status, categoryId })

  const columns: Column<StockLevel>[] = [
    { key: 'sku', header: 'SKU', sortKey: 'sku', cell: (s) => <span className="font-mono text-xs">{s.sku}</span>, className: 'w-36' },
    { key: 'name', header: 'Product', sortKey: 'name', cell: (s) => <span className="font-medium">{s.name}</span> },
    { key: 'category', header: 'Category', cell: (s) => s.categoryName },
    { key: 'onHand', header: 'On hand', sortKey: 'quantityOnHand', align: 'right', cell: (s) => formatQuantity(s.quantityOnHand, s.unit) },
    { key: 'reorder', header: 'Reorder at', align: 'right', cell: (s) => formatQuantity(s.reorderLevel, s.unit) },
    { key: 'status', header: 'Status', cell: (s) => <StockStatusBadge status={s.status} /> },
    { key: 'last', header: 'Last movement', sortKey: 'lastMovement', cell: (s) => formatDate(s.lastMovementAtUtc) },
    ...(showCost ? [{ key: 'value', header: 'Value at cost', align: 'right' as const, cell: (s: StockLevel) => formatINR(s.stockValue, 0) }] : []),
    ...(canEdit
      ? [
          {
            key: 'actions',
            header: '',
            className: 'w-24',
            cell: (s: StockLevel) => (
              <Button
                variant="ghost"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  onAdjust({ id: s.productId, sku: s.sku, name: s.name, unit: s.unit, quantityOnHand: s.quantityOnHand, price: 0, gstRate: 0 })
                }}
              >
                Adjust
              </Button>
            ),
          },
        ]
      : []),
  ]

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search SKU or product" />
        <Select value={status ?? ALL} onValueChange={(v) => update({ filters: { status: v === ALL ? undefined : v } })}>
          <SelectTrigger className="w-44" aria-label="Filter by stock status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All statuses</SelectItem>
            <SelectItem value="Low">Low (incl. out)</SelectItem>
            <SelectItem value="OutOfStock">Out of stock</SelectItem>
            <SelectItem value="InStock">In stock</SelectItem>
          </SelectContent>
        </Select>
        <Select value={categoryId?.toString() ?? ALL} onValueChange={(v) => update({ filters: { categoryId: v === ALL ? undefined : v } })}>
          <SelectTrigger className="w-52" aria-label="Filter by category">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All categories</SelectItem>
            {categories.data?.map((c) => (
              <SelectItem key={c.id} value={c.id.toString()}>
                {c.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <DataTable
        columns={columns}
        rows={levels.data?.items}
        getRowKey={(s) => s.productId}
        isLoading={levels.isPending}
        error={levels.error}
        sort={params.sort}
        onSortChange={(sort) => update({ sort })}
        onRowClick={(s) => navigate(`/stock?tab=movements&productId=${s.productId}&product=${encodeURIComponent(`${s.sku} · ${s.name}`)}`)}
        emptyTitle={status ? 'Nothing matches this filter' : 'No products yet'}
      />
      {levels.data && (
        <Pagination
          page={levels.data.page}
          pageSize={levels.data.pageSize}
          totalCount={levels.data.totalCount}
          totalPages={levels.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}

function MovementsTab() {
  const [params, update] = useListParams({ pageSize: 25 })
  const productId = params.filters.productId ? Number(params.filters.productId) : undefined
  const type = params.filters.type as StockMovementType | undefined
  const from = params.filters.from
  const to = params.filters.to

  const movements = useStockMovements({ q: params.q || undefined, page: params.page, pageSize: params.pageSize, productId, type, from, to })

  const columns: Column<StockMovement>[] = [
    { key: 'date', header: 'Date', cell: (m) => formatBusinessDate(m.movementDate), className: 'w-28' },
    {
      key: 'product',
      header: 'Product',
      cell: (m) => (
        <div className="flex flex-col">
          <span className="font-medium">{m.productName}</span>
          <span className="font-mono text-xs text-muted-foreground">{m.sku}</span>
        </div>
      ),
    },
    { key: 'type', header: 'Type', cell: (m) => <MovementTypeBadge type={m.type} /> },
    { key: 'qty', header: 'Quantity', align: 'right', cell: (m) => <SignedQuantity quantity={m.quantity} unit={m.unit} /> },
    { key: 'balance', header: 'Balance', align: 'right', cell: (m) => formatQuantity(m.balanceAfter, m.unit) },
    {
      key: 'ref',
      header: 'Reference',
      cell: (m) =>
        m.referenceType === 'Receipt' && m.referenceId ? (
          <Link className="font-mono text-xs underline-offset-4 hover:underline" to={`/stock/receipts/${m.referenceId}`} onClick={(e) => e.stopPropagation()}>
            {m.referenceNumber}
          </Link>
        ) : (
          <span className="text-muted-foreground">{reasonLabel(m.reason) || m.referenceNumber || '—'}</span>
        ),
    },
    { key: 'notes', header: 'Notes', cell: (m) => <span className="line-clamp-2 text-muted-foreground">{m.notes ?? ''}</span> },
    { key: 'by', header: 'By', cell: (m) => m.createdByName },
  ]

  return (
    <>
      <div className="flex flex-wrap items-end gap-3">
        {productId ? (
          <Button variant="secondary" size="sm" onClick={() => update({ filters: { productId: undefined, product: undefined } })}>
            {params.filters.product ?? `Product ${productId}`}
            <X />
          </Button>
        ) : (
          <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search product or reference" />
        )}
        <Select value={type ?? ALL} onValueChange={(v) => update({ filters: { type: v === ALL ? undefined : v } })}>
          <SelectTrigger className="w-44" aria-label="Filter by movement type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All types</SelectItem>
            {Object.entries(movementTypeLabels).map(([value, label]) => (
              <SelectItem key={value} value={value}>
                {label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Label htmlFor="from" className="text-muted-foreground">
            From
          </Label>
          <Input id="from" type="date" className="w-40" value={from ?? ''} onChange={(e) => update({ filters: { from: e.target.value || undefined } })} />
          <Label htmlFor="to" className="text-muted-foreground">
            to
          </Label>
          <Input id="to" type="date" className="w-40" value={to ?? ''} onChange={(e) => update({ filters: { to: e.target.value || undefined } })} />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={movements.data?.items}
        getRowKey={(m) => m.id}
        isLoading={movements.isPending}
        error={movements.error}
        emptyTitle="No stock movements"
        emptyDescription="Receipts and adjustments appear here, and sales once invoicing goes live."
      />
      {movements.data && (
        <Pagination
          page={movements.data.page}
          pageSize={movements.data.pageSize}
          totalCount={movements.data.totalCount}
          totalPages={movements.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}

function ReceiptsTab({ canEdit, showCost }: { canEdit: boolean; showCost: boolean }) {
  const navigate = useNavigate()
  const [params, update] = useListParams()
  const receipts = useStockReceipts({ q: params.q || undefined, page: params.page, pageSize: params.pageSize })

  const columns: Column<StockReceiptListItem>[] = [
    { key: 'number', header: 'GRN', cell: (r) => <span className="font-mono text-xs">{r.number}</span>, className: 'w-28' },
    { key: 'date', header: 'Date', cell: (r) => formatBusinessDate(r.receiptDate) },
    { key: 'supplier', header: 'Supplier', cell: (r) => r.supplierName ?? '—' },
    { key: 'ref', header: 'Supplier ref', cell: (r) => r.supplierReference ?? '—' },
    { key: 'lines', header: 'Lines', align: 'right', cell: (r) => r.lineCount },
    ...(showCost ? [{ key: 'cost', header: 'Total cost', align: 'right' as const, cell: (r: StockReceiptListItem) => formatINR(r.totalCost) }] : []),
    { key: 'by', header: 'Received by', cell: (r) => r.createdByName },
  ]

  return (
    <>
      <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search GRN, supplier or supplier ref" />
      <DataTable
        columns={columns}
        rows={receipts.data?.items}
        getRowKey={(r) => r.id}
        isLoading={receipts.isPending}
        error={receipts.error}
        onRowClick={(r) => navigate(`/stock/receipts/${r.id}`)}
        emptyTitle="No goods receipts yet"
        emptyDescription={canEdit ? 'Use "Receive stock" when goods arrive.' : undefined}
      />
      {receipts.data && (
        <Pagination
          page={receipts.data.page}
          pageSize={receipts.data.pageSize}
          totalCount={receipts.data.totalCount}
          totalPages={receipts.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}

export function StockPage() {
  const { can } = useAuth()
  const canEdit = can(Permissions.StockEdit)
  const showCost = can(Permissions.CostPricesView)
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = (searchParams.get('tab') as Tab | null) ?? 'levels'
  const [adjusting, setAdjusting] = useState<{ open: boolean; product: PickedProduct | null }>({ open: false, product: null })

  return (
    <>
      <PageHeader
        title="Stock"
        description="Every change is recorded in the stock ledger, so on-hand figures can always be explained."
        actions={
          canEdit && (
            <>
              <Button variant="outline" onClick={() => setAdjusting({ open: true, product: null })}>
                <SlidersHorizontal />
                Adjust stock
              </Button>
              <Button asChild>
                <Link to="/stock/receipts/new">
                  <PackagePlus />
                  Receive stock
                </Link>
              </Button>
            </>
          )
        }
      />

      <SummaryCards />

      {/* Switching tabs starts that tab with fresh filters. */}
      <Tabs value={tab} onValueChange={(next) => setSearchParams({ tab: next })}>
        <TabsList>
          <TabsTrigger value="levels">Stock levels</TabsTrigger>
          <TabsTrigger value="movements">Ledger</TabsTrigger>
          <TabsTrigger value="receipts">Goods receipts</TabsTrigger>
        </TabsList>
        <TabsContent value="levels" className="flex flex-col gap-4 pt-2">
          {tab === 'levels' && <LevelsTab canEdit={canEdit} showCost={showCost} onAdjust={(product) => setAdjusting({ open: true, product })} />}
        </TabsContent>
        <TabsContent value="movements" className="flex flex-col gap-4 pt-2">
          {tab === 'movements' && <MovementsTab />}
        </TabsContent>
        <TabsContent value="receipts" className="flex flex-col gap-4 pt-2">
          {tab === 'receipts' && <ReceiptsTab canEdit={canEdit} showCost={showCost} />}
        </TabsContent>
      </Tabs>

      {canEdit && (
        <AdjustStockDialog open={adjusting.open} product={adjusting.product} onOpenChange={(open) => setAdjusting((a) => ({ ...a, open }))} />
      )}
    </>
  )
}
