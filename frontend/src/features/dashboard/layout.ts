import { Permissions } from '@/features/auth/permissions'
import type { Schemas } from '@/lib/api/client'
import { msg } from '@/lib/i18n'

export type DashboardPeriod = Schemas['DashboardPeriod']

export const TILE_IDS = ['sales', 'collected', 'receivables', 'orders', 'stock'] as const
export const CARD_IDS = ['trend', 'ageing', 'top-customers', 'top-products', 'states', 'chase', 'payments', 'purchases', 'quick-actions', 'notifications'] as const

export type TileId = (typeof TILE_IDS)[number]
export type CardId = (typeof CARD_IDS)[number]
export type CardSize = 'normal' | 'wide'

export interface TilePlacement {
  id: TileId
  hidden?: boolean
}

export interface CardPlacement {
  id: CardId
  size: CardSize
  hidden?: boolean
}

/** One person's dashboard: which tiles and cards, in what order and size, for what period. Saved on the server. */
export interface DashboardLayout {
  period: DashboardPeriod
  /** The 3D city at the top. */
  city: boolean
  tiles: TilePlacement[]
  cards: CardPlacement[]
}

/**
 * label for wide screens, short for phones, and the sentences that name the period, written out whole for each
 * period so they read naturally in Tamil too (translate with t() where shown).
 */
export interface PeriodInfo {
  value: DashboardPeriod
  label: string
  short: string
  sales: string
  collected: string
  noSales: string
  supply: string
  /** Beside the change in sales ("+12% vs same days last month"). */
  versus: string
  noComparison: string
}

export const PERIODS: PeriodInfo[] = [
  {
    value: 'Month',
    label: msg('This month'),
    short: msg('Month'),
    sales: msg('Sales this month'),
    collected: msg('Collected this month'),
    noSales: msg('No sales this month yet.'),
    supply: msg('Place of supply, this month'),
    versus: msg('vs same days last month'),
    noComparison: msg('No sales same days last month to compare'),
  },
  {
    value: 'Quarter',
    label: msg('This quarter'),
    short: msg('Quarter'),
    sales: msg('Sales this quarter'),
    collected: msg('Collected this quarter'),
    noSales: msg('No sales this quarter yet.'),
    supply: msg('Place of supply, this quarter'),
    versus: msg('vs same days last quarter'),
    noComparison: msg('No sales same days last quarter to compare'),
  },
  {
    value: 'Year',
    label: msg('This financial year'),
    short: msg('This FY'),
    sales: msg('Sales this financial year'),
    collected: msg('Collected this financial year'),
    noSales: msg('No sales this financial year yet.'),
    supply: msg('Place of supply, this financial year'),
    versus: msg('vs same period last year'),
    noComparison: msg('No sales same period last year to compare'),
  },
  {
    value: 'LastYear',
    label: msg('Last financial year'),
    short: msg('Last FY'),
    sales: msg('Sales last financial year'),
    collected: msg('Collected last financial year'),
    noSales: msg('No sales last financial year yet.'),
    supply: msg('Place of supply, last financial year'),
    versus: msg('vs the year before'),
    noComparison: msg('No sales the year before to compare'),
  },
]

export const periodInfo = (period: DashboardPeriod) => PERIODS.find((p) => p.value === period) ?? PERIODS[0]!

/** Tile and card names are marked here; translate them with t() where they're shown. */
export const TILE_TITLES: Record<TileId, string> = {
  sales: msg('Sales'),
  collected: msg('Money collected'),
  receivables: msg('Receivables'),
  orders: msg('Confirmed orders to invoice'),
  stock: msg('Low on stock'),
}

/** What each card is, and who may have it (any one of the permissions; none listed: everyone). */
export const CARD_INFO: Record<CardId, { title: string; description: string; permissions?: string[] }> = {
  trend: { title: msg('Monthly sales'), description: msg('The last 24 months as a chart or table.') },
  ageing: { title: msg('Receivables ageing'), description: msg("What's owed, by how late it is.") },
  'top-customers': { title: msg('Top customers'), description: msg('Who bought the most in the period.') },
  'top-products': { title: msg('Top products'), description: msg('What sold the most in the period.') },
  states: { title: msg('Sales by state'), description: msg('Where the period’s sales went.') },
  chase: { title: msg('Invoices to chase'), description: msg('The most overdue invoices, with a payment reminder.'), permissions: [Permissions.InvoicesView] },
  payments: { title: msg('Recent payments'), description: msg('The latest money received from customers.'), permissions: [Permissions.PaymentsView] },
  purchases: {
    title: msg('Purchases needing action'),
    description: msg('Orders waiting for approval and supplier bills on hold or overdue.'),
    permissions: [Permissions.PurchaseOrdersView, Permissions.SupplierBillsView],
  },
  'quick-actions': { title: msg('Quick actions'), description: msg('Big buttons for what you do most.') },
  notifications: { title: msg('Notifications'), description: msg('Your latest notifications, as in the bell.') },
}

export const DEFAULT_LAYOUT: DashboardLayout = {
  period: 'Month',
  city: true,
  tiles: TILE_IDS.map((id) => ({ id })),
  cards: [
    { id: 'trend', size: 'wide' },
    { id: 'ageing', size: 'normal' },
    { id: 'top-customers', size: 'normal' },
    { id: 'top-products', size: 'normal' },
    { id: 'states', size: 'normal' },
    // New cards start hidden; anyone can add them in Customise.
    { id: 'chase', size: 'normal', hidden: true },
    { id: 'payments', size: 'normal', hidden: true },
    { id: 'purchases', size: 'normal', hidden: true },
    { id: 'quick-actions', size: 'normal', hidden: true },
    { id: 'notifications', size: 'normal', hidden: true },
  ],
}

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null

/**
 * A saved layout made safe to use: unknown entries dropped, duplicates removed, and anything added to the app since
 * it was saved put back in its default place (hidden if it starts hidden).
 */
export function normaliseLayout(saved: unknown): DashboardLayout {
  if (!isRecord(saved)) return DEFAULT_LAYOUT
  const period = PERIODS.some((p) => p.value === saved.period) ? (saved.period as DashboardPeriod) : DEFAULT_LAYOUT.period

  const merge = <T extends { id: string }>(list: unknown, ids: readonly string[], defaults: T[], read: (entry: Record<string, unknown>, fallback: T) => T): T[] => {
    const seen = new Set<string>()
    const kept: T[] = []
    for (const entry of Array.isArray(list) ? list : []) {
      if (!isRecord(entry) || typeof entry.id !== 'string' || !ids.includes(entry.id) || seen.has(entry.id)) continue
      seen.add(entry.id)
      kept.push(read(entry, defaults.find((d) => d.id === entry.id)!))
    }
    for (const fallback of defaults) if (!seen.has(fallback.id)) kept.push(fallback)
    return kept
  }

  return {
    period,
    city: typeof saved.city === 'boolean' ? saved.city : true,
    tiles: merge(saved.tiles, TILE_IDS, DEFAULT_LAYOUT.tiles, (entry, fallback) => ({ id: fallback.id, hidden: entry.hidden === true })),
    cards: merge(saved.cards, CARD_IDS, DEFAULT_LAYOUT.cards, (entry, fallback) => ({
      id: fallback.id,
      size: entry.size === 'wide' ? 'wide' : 'normal',
      hidden: entry.hidden === true,
    })),
  }
}

/** Cards this person may have at all (the rest are neither shown nor offered). */
export function allowedCards(layout: DashboardLayout, can: (...anyOf: string[]) => boolean): CardPlacement[] {
  return layout.cards.filter((card) => !CARD_INFO[card.id].permissions || can(...CARD_INFO[card.id].permissions!))
}

/** Moves an item to where another one is (dragging one onto another). */
export function moveTo<T extends { id: string }>(list: T[], id: string, overId: string): T[] {
  const from = list.findIndex((item) => item.id === id)
  const to = list.findIndex((item) => item.id === overId)
  if (from < 0 || to < 0 || from === to) return list
  const next = [...list]
  next.splice(to, 0, ...next.splice(from, 1))
  return next
}
