import { createContext, useContext } from 'react'
import type { Location } from 'react-router'

/**
 * Lists whose records open in a slide-over panel beside them (their detail pages are child routes). Smart order
 * entry lives under /orders but is a page of its own.
 */
export const PANEL_LISTS = [
  '/customers',
  '/suppliers',
  '/products',
  '/orders',
  '/invoices',
  '/credit-notes',
  '/purchase-orders',
  '/supplier-bills',
  '/supplier-returns',
  '/stock',
] as const

const ownPages = new Set(['/orders/smart-entry'])

/** The list a path belongs to when it's that list or one of its panels, e.g. /invoices/42 -> /invoices. */
export function panelListOf(pathname: string): string | undefined {
  if (ownPages.has(pathname)) return undefined
  return PANEL_LISTS.find((list) => pathname === list || pathname.startsWith(`${list}/`))
}

/**
 * Scroll positions are kept per list rather than per visit, so opening or switching records in the panel never
 * scrolls the list behind it.
 */
export function scrollKey(location: Location): string {
  return panelListOf(location.pathname) ?? location.key
}

/** The record open in the panel (the first part of its path, usually its id), so the list can mark its row. */
export const OpenRecordContext = createContext<string | undefined>(undefined)

export function useOpenRecordId(): string | undefined {
  return useContext(OpenRecordContext)
}

/** The list's table reports its rows' ids here, so the panel can step to the next and previous record. */
export const ListRowsContext = createContext<((ids: string[]) => void) | null>(null)

/** True inside the side panel, which scrolls on its own: page effects tied to the window's scroll stay off there. */
export const InPanelContext = createContext(false)

export function useInPanel(): boolean {
  return useContext(InPanelContext)
}

let morphOrigin: { rect: DOMRect; at: number } | null = null

/** Where the clicked row was on screen, so the panel can grow out of it. */
export function setMorphOrigin(rect: DOMRect | null) {
  morphOrigin = rect && { rect, at: performance.now() }
}

/** The row clicked a moment ago, if any: a panel opened any other way (a link, Back) just slides in. */
export function recentMorphOrigin(): DOMRect | null {
  return morphOrigin && performance.now() - morphOrigin.at < 1500 ? morphOrigin.rect : null
}
