import { Permissions } from '@/features/auth/permissions'
import type { Schemas } from '@/lib/api/client'

export type DashboardPeriod = Schemas['DashboardPeriod']

export const TILE_IDS = ['sales', 'collected', 'receivables', 'orders', 'stock'] as const
export const CARD_IDS = ['trend', 'ageing', 'top-customers', 'top-products', 'states', 'chase', 'payments', 'purchases', 'quick-actions'] as const

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

/** label for wide screens, short for phones, text inside sentences ("Sales this month"). */
export const PERIODS: { value: DashboardPeriod; label: string; short: string; text: string; comparison: string }[] = [
  { value: 'Month', label: 'This month', short: 'Month', text: 'this month', comparison: 'same days last month' },
  { value: 'Quarter', label: 'This quarter', short: 'Quarter', text: 'this quarter', comparison: 'same days last quarter' },
  { value: 'Year', label: 'This financial year', short: 'This FY', text: 'this financial year', comparison: 'same period last year' },
  { value: 'LastYear', label: 'Last financial year', short: 'Last FY', text: 'last financial year', comparison: 'the year before' },
]

export const periodInfo = (period: DashboardPeriod) => PERIODS.find((p) => p.value === period) ?? PERIODS[0]!

export const TILE_TITLES: Record<TileId, string> = {
  sales: 'Sales',
  collected: 'Money collected',
  receivables: 'Receivables',
  orders: 'Confirmed orders to invoice',
  stock: 'Low on stock',
}

/** What each card is, and who may have it (any one of the permissions; none listed: everyone). */
export const CARD_INFO: Record<CardId, { title: string; description: string; permissions?: string[] }> = {
  trend: { title: 'Monthly sales', description: 'The last 24 months as a chart or table.' },
  ageing: { title: 'Receivables ageing', description: "What's owed, by how late it is." },
  'top-customers': { title: 'Top customers', description: 'Who bought the most in the period.' },
  'top-products': { title: 'Top products', description: 'What sold the most in the period.' },
  states: { title: 'Sales by state', description: 'Where the period’s sales went.' },
  chase: { title: 'Invoices to chase', description: 'The most overdue invoices, with a payment reminder.', permissions: [Permissions.InvoicesView] },
  payments: { title: 'Recent payments', description: 'The latest money received from customers.', permissions: [Permissions.PaymentsView] },
  purchases: {
    title: 'Purchases needing action',
    description: 'Orders waiting for approval and supplier bills on hold or overdue.',
    permissions: [Permissions.PurchaseOrdersView, Permissions.SupplierBillsView],
  },
  'quick-actions': { title: 'Quick actions', description: 'Big buttons for what you do most.' },
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
