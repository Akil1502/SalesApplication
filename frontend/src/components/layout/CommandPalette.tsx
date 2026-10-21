import { useQuery } from '@tanstack/react-query'
import {
  ClipboardList,
  Factory,
  LayoutPanelLeft,
  Moon,
  Package,
  PanelBottom,
  PanelTop,
  Plus,
  ReceiptIndianRupee,
  ShoppingCart,
  Sun,
  Users,
  Clock,
  Star,
  type LucideIcon,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { usePreferences } from '@/components/fx/preferences-context'
import { Command, CommandDialog, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList, CommandShortcut } from '@/components/ui/command'
import { Spinner } from '@/components/ui/spinner'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { api } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { formatINR } from '@/lib/format'
import { t } from '@/lib/i18n'
import { useDebounced } from '@/lib/use-debounced'
import { useFavourites, useRecent } from './record-memory'
import { useNav } from './use-nav'

/** Every word of the query appears in the item's text. */
function matchesWords(value: string, query: string): boolean {
  const text = value.toLowerCase()
  return query
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => text.includes(word))
}

interface Hit {
  key: string
  label: string
  detail?: string
  icon: LucideIcon
  to: string
}

/** Up to five records of each kind matching the search, from the lists the user may see. */
function useRecordSearch(q: string, enabled: boolean) {
  const { can } = useAuth()
  const page = { q, page: 1, pageSize: 5 }
  return useQuery({
    queryKey: ['command-search', q],
    enabled: enabled && q.length >= 2,
    staleTime: 30_000,
    queryFn: async (): Promise<{ group: string; hits: Hit[] }[]> => {
      const searches: Promise<{ group: string; hits: Hit[] }>[] = []
      if (can(Permissions.CustomersView)) {
        searches.push(
          unwrap(api.GET('/api/customers', { params: { query: page } })).then((r) => ({
            group: t('Customers'),
            hits: r.items.map((c) => ({ key: `c${c.id}`, label: c.name, detail: `${c.city}, ${c.stateName}`, icon: Users, to: `/customers/${c.id}` })),
          })),
        )
      }
      if (can(Permissions.InvoicesView)) {
        searches.push(
          unwrap(api.GET('/api/invoices', { params: { query: page } })).then((r) => ({
            group: t('Invoices'),
            hits: r.items.map((i) => ({ key: `i${i.id}`, label: i.number, detail: `${i.customerName} · ${formatINR(i.grandTotal)}`, icon: ReceiptIndianRupee, to: `/invoices/${i.id}` })),
          })),
        )
      }
      if (can(Permissions.OrdersView)) {
        searches.push(
          unwrap(api.GET('/api/orders', { params: { query: page } })).then((r) => ({
            group: t('Orders'),
            hits: r.items.map((o) => ({ key: `o${o.id}`, label: o.number, detail: `${o.customerName} · ${formatINR(o.grandTotal)}`, icon: ShoppingCart, to: `/orders/${o.id}` })),
          })),
        )
      }
      if (can(Permissions.ProductsView)) {
        searches.push(
          unwrap(api.GET('/api/products', { params: { query: page } })).then((r) => ({
            group: t('Products'),
            hits: r.items.map((p) => ({ key: `p${p.id}`, label: p.name, detail: `${p.sku} · ${formatINR(p.price)} / ${p.unit}`, icon: Package, to: `/products/${p.id}` })),
          })),
        )
      }
      if (can(Permissions.SuppliersView)) {
        searches.push(
          unwrap(api.GET('/api/suppliers', { params: { query: page } })).then((r) => ({
            group: t('Suppliers'),
            hits: r.items.map((s) => ({ key: `s${s.id}`, label: s.name, detail: `${s.city}, ${s.stateName}`, icon: Factory, to: `/suppliers/${s.id}` })),
          })),
        )
      }
      // One kind failing (or forbidden) doesn't hide the others.
      const results = await Promise.allSettled(searches)
      return results.flatMap((result) => (result.status === 'fulfilled' && result.value.hits.length > 0 ? [result.value] : []))
    },
  })
}

/**
 * Ctrl+K (or ⌘K) from anywhere: jump to a page, start something new, find a customer, invoice, order, product or
 * supplier by name or number, or change the layout and theme.
 */
export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const { can } = useAuth()
  const { groups } = useNav()
  const preferences = usePreferences()
  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim(), 200)
  const records = useRecordSearch(q, open)
  const { favourites } = useFavourites()
  const { recent } = useRecent()
  const recentOnly = recent.filter((r) => !favourites.some((f) => f.path === r.path))

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        onOpenChange(!open)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onOpenChange])

  const run = (action: () => void) => {
    onOpenChange(false)
    setSearch('')
    action()
  }

  const create: Hit[] = [
    ...(can(Permissions.OrdersEdit) ? [{ key: 'new-order', label: t('New order'), icon: ShoppingCart, to: '/orders/new' }] : []),
    ...(can(Permissions.CustomersEdit) ? [{ key: 'new-customer', label: t('New customer'), icon: Users, to: '/customers/new' }] : []),
    ...(can(Permissions.ProductsEdit) ? [{ key: 'new-product', label: t('New product'), icon: Package, to: '/products/new' }] : []),
    ...(can(Permissions.PurchaseOrdersEdit) ? [{ key: 'new-po', label: t('New purchase order'), icon: ClipboardList, to: '/purchase-orders/new' }] : []),
  ]

  return (
    <CommandDialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        if (!next) setSearch('')
      }}
      title={t('Search')}
      description={t('Jump to a page or find a record')}
      className="sm:max-w-xl"
    >
      {/* Plain word matching rather than cmdk's fuzzy letters, so "pandian" doesn't match "Company settings". */}
      <Command filter={(value, query) => (matchesWords(value, query) ? 1 : 0)}>
        <CommandInput placeholder={t('Search pages, customers, invoices, orders, products…')} value={search} onValueChange={setSearch} />
        <CommandList className="max-h-[min(28rem,60vh)]">
          <CommandEmpty>{records.isFetching ? t('Searching…') : t('Nothing matches.')}</CommandEmpty>

          {favourites.length > 0 && (
            <CommandGroup heading={t('Favourites')}>
              {favourites.map((item) => (
                <CommandItem key={`fav-${item.path}`} value={`${item.label} ${item.list} ${t(item.list)} favourite ${item.path}`} onSelect={() => run(() => navigate(item.path))}>
                  <Star className="fill-amber-400 text-amber-500" />
                  <span className="truncate">{item.label}</span>
                  <span className="ml-auto text-xs text-muted-foreground">{t(item.list)}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}
          {recentOnly.length > 0 && !search.trim() && (
            <CommandGroup heading={t('Recently viewed')}>
              {recentOnly.slice(0, 6).map((item) => (
                <CommandItem key={`recent-${item.path}`} value={`${item.label} ${item.list} ${t(item.list)} recent ${item.path}`} onSelect={() => run(() => navigate(item.path))}>
                  <Clock />
                  <span className="truncate">{item.label}</span>
                  <span className="ml-auto text-xs text-muted-foreground">{t(item.list)}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}

          {records.data?.map((result) => (
            <CommandGroup key={result.group} heading={result.group}>
              {result.hits.map((hit) => (
                // The server already matched these (by GSTIN, phone and so on too), so the search text is in the value.
                <CommandItem key={hit.key} value={`${q} ${hit.label} ${hit.detail ?? ''} ${hit.key}`} onSelect={() => run(() => navigate(hit.to))}>
                  <hit.icon />
                  <span className="truncate">{hit.label}</span>
                  {hit.detail && <span className="ml-auto truncate text-xs text-muted-foreground">{hit.detail}</span>}
                </CommandItem>
              ))}
            </CommandGroup>
          ))}
          {records.isFetching && !records.data && q.length >= 2 && (
            <div className="flex items-center justify-center gap-2 py-3 text-xs text-muted-foreground">
              <Spinner className="size-3.5" /> {t('Searching records…')}
            </div>
          )}

          {groups.map((group) => (
            <CommandGroup key={group.label} heading={group.label}>
              {group.items.map((item) => (
                <CommandItem key={item.to} value={`${item.title} ${item.description} ${item.english} ${group.label}`} onSelect={() => run(() => navigate(item.to))}>
                  <item.icon />
                  <span>{item.title}</span>
                  <span className="ml-auto truncate text-xs text-muted-foreground">{item.description}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          ))}

          {create.length > 0 && (
            <CommandGroup heading={t('Create')}>
              {create.map((hit) => (
                <CommandItem key={hit.key} value={hit.label} onSelect={() => run(() => navigate(hit.to))}>
                  <Plus />
                  <span>{hit.label}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          )}

          <CommandGroup heading={t('Preferences')}>
            <CommandItem value={`Switch theme dark light mode ${t('Switch to light mode')} ${t('Switch to dark mode')}`} onSelect={() => run(() => preferences.setTheme(preferences.resolvedTheme === 'dark' ? 'light' : 'dark'))}>
              {preferences.resolvedTheme === 'dark' ? <Sun /> : <Moon />}
              {preferences.resolvedTheme === 'dark' ? t('Switch to light mode') : t('Switch to dark mode')}
            </CommandItem>
            {(
              [
                { value: 'rail', label: t('Layout: icon rail'), icon: LayoutPanelLeft },
                { value: 'top', label: t('Layout: top menus'), icon: PanelTop },
                { value: 'dock', label: t('Layout: dock'), icon: PanelBottom },
              ] as const
            ).map((layout) => (
              <CommandItem key={layout.value} value={layout.label} onSelect={() => run(() => preferences.setLayout(layout.value))}>
                <layout.icon />
                {layout.label}
                {preferences.layout === layout.value && <CommandShortcut>{t('Current')}</CommandShortcut>}
              </CommandItem>
            ))}
          </CommandGroup>
        </CommandList>
      </Command>
    </CommandDialog>
  )
}
