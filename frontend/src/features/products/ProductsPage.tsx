import { Plus, TriangleAlert } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { ActiveBadge } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { formatINR, formatPercent, formatQuantity } from '@/lib/format'
import { useCategories } from '@/lib/lookups'
import { useProducts, type ProductListItem } from './api'

const ALL = 'all'

const columns: Column<ProductListItem>[] = [
  { key: 'sku', header: 'SKU', sortKey: 'sku', cell: (p) => <span className="font-mono text-xs">{p.sku}</span>, className: 'w-36' },
  { key: 'name', header: 'Product', sortKey: 'name', cell: (p) => <span className="font-medium">{p.name}</span> },
  { key: 'category', header: 'Category', sortKey: 'category', cell: (p) => p.categoryName },
  { key: 'hsn', header: 'HSN · GST', cell: (p) => <span className="tabular-nums">{p.hsnCode} · {formatPercent(p.gstRate)}</span> },
  {
    key: 'price',
    header: 'Price today',
    align: 'right',
    cell: (p) => (
      <span className="inline-flex items-center gap-1.5">
        {p.hasRate && (
          <Badge variant="outline" title={`From the rate list; list price ${formatINR(p.listPrice)}`}>
            Rate list
          </Badge>
        )}
        {formatINR(p.price)} / {p.unit}
      </span>
    ),
  },
  {
    key: 'stock',
    header: 'In stock',
    sortKey: 'quantityOnHand',
    align: 'right',
    cell: (p) => (
      <span className={p.quantityOnHand <= p.reorderLevel ? 'inline-flex items-center gap-1 text-amber-700 dark:text-amber-400' : ''}>
        {p.quantityOnHand <= p.reorderLevel && <TriangleAlert className="size-3.5" aria-label="At or below reorder level" />}
        {formatQuantity(p.quantityOnHand, p.unit)}
      </span>
    ),
  },
  { key: 'status', header: 'Status', cell: (p) => <ActiveBadge active={p.isActive} /> },
]

export function ProductsPage() {
  const navigate = useNavigate()
  const { can } = useAuth()
  const canEdit = can(Permissions.ProductsEdit)
  const [params, update] = useListParams({ sort: 'name' })
  const categories = useCategories()
  const categoryId = params.filters.categoryId ? Number(params.filters.categoryId) : undefined
  const lowStockOnly = params.filters.lowStock === 'true'

  const products = useProducts({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    sort: params.sort,
    includeInactive: params.includeInactive,
    categoryId,
    lowStockOnly,
  })

  const filtered = !!params.q || categoryId !== undefined || lowStockOnly

  return (
    <>
      <PageHeader
        title="Products"
        description="Prices exclude GST. The HSN code sets the GST rate on invoices."
        actions={
          canEdit && (
            <Button asChild>
              <Link to="/products/new">
                <Plus />
                New product
              </Link>
            </Button>
          )
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search SKU, name or HSN" />
        <Select
          value={categoryId?.toString() ?? ALL}
          onValueChange={(value) => update({ filters: { categoryId: value === ALL ? undefined : value } })}
        >
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
        <div className="flex items-center gap-2">
          <Switch id="low-stock" checked={lowStockOnly} onCheckedChange={(on) => update({ filters: { lowStock: on ? 'true' : undefined } })} />
          <Label htmlFor="low-stock">Low stock only</Label>
        </div>
        <div className="flex items-center gap-2">
          <Switch id="show-inactive" checked={params.includeInactive} onCheckedChange={(includeInactive) => update({ includeInactive })} />
          <Label htmlFor="show-inactive">Show inactive</Label>
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={products.data?.items}
        getRowKey={(p) => p.id}
        isLoading={products.isPending}
        error={products.error}
        onRowClick={(p) => navigate(`/products/${p.id}`)}
        sort={params.sort}
        onSortChange={(sort) => update({ sort })}
        emptyTitle={filtered ? 'No products match' : 'No products yet'}
        emptyDescription={filtered ? 'Try a different search or filter.' : canEdit ? 'Add products to start quoting and invoicing.' : undefined}
      />

      {products.data && (
        <Pagination
          page={products.data.page}
          pageSize={products.data.pageSize}
          totalCount={products.data.totalCount}
          totalPages={products.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
    </>
  )
}
