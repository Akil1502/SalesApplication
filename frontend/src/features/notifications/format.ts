import { AlarmClock, ClipboardCheck, ClipboardPlus, FileWarning, HandCoins, PackageMinus, ShoppingBag, Sparkles, type LucideIcon } from 'lucide-react'
import { formatDateParts } from '@/lib/format'
import { getLanguage, msg, t } from '@/lib/i18n'
import type { Notification, NotificationKind } from './api'

/** Each kind's icon and colours (tinted tile, readable in light and dark). */
export const KIND_LOOK: Record<NotificationKind, { icon: LucideIcon; tile: string }> = {
  PaymentReceived: { icon: HandCoins, tile: 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300' },
  OrderCreated: { icon: ClipboardPlus, tile: 'bg-primary/12 text-primary' },
  OrderConfirmed: { icon: ClipboardCheck, tile: 'bg-primary/12 text-primary' },
  OverdueSummary: { icon: AlarmClock, tile: 'bg-red-500/12 text-red-700 dark:text-red-300' },
  LowStock: { icon: PackageMinus, tile: 'bg-amber-500/15 text-amber-700 dark:text-amber-300' },
  PurchaseOrderApproval: { icon: ShoppingBag, tile: 'bg-violet-500/15 text-violet-700 dark:text-violet-300' },
  BillApproval: { icon: FileWarning, tile: 'bg-amber-500/15 text-amber-700 dark:text-amber-300' },
  InsightsReady: { icon: Sparkles, tile: 'bg-teal-500/15 text-teal-700 dark:text-teal-300' },
}

const IST = 'Asia/Kolkata'
const dayOf = (date: Date) => new Intl.DateTimeFormat('en-CA', { timeZone: IST, year: 'numeric', month: '2-digit', day: '2-digit' }).format(date)
const clock = (date: Date) => {
  const time = formatDateParts(date, { timeZone: IST, hour: 'numeric', minute: '2-digit', hour12: true })
  return getLanguage() === 'en' ? time.toLowerCase() : time
}

function yesterdayOf(now: Date): string {
  return dayOf(new Date(now.getTime() - 24 * 60 * 60 * 1000))
}

/** When it happened, as people say it: "Just now", "12 min ago", "4:05 pm", "Yesterday, 4:05 pm", "12 Oct". */
export function whenText(iso: string, now = new Date()): string {
  const at = new Date(iso)
  const minutes = Math.floor((now.getTime() - at.getTime()) / 60_000)
  if (minutes < 1) return t('Just now')
  if (minutes < 60) return t('{minutes} min ago', { minutes })
  if (dayOf(at) === dayOf(now)) return clock(at)
  if (dayOf(at) === yesterdayOf(now)) return t('Yesterday, {time}', { time: clock(at) })
  return formatDateParts(at, { timeZone: IST, day: 'numeric', month: 'short' })
}

export type DayGroup = { label: 'Today' | 'Yesterday' | 'Earlier'; items: Notification[] }

/** Newest first, under Today, Yesterday and Earlier (India time); empty groups are left out. Labels are marked with msg(). */
export function groupByDay(items: Notification[], now = new Date()): DayGroup[] {
  const today = dayOf(now)
  const yesterday = yesterdayOf(now)
  const groups: DayGroup[] = [
    { label: msg('Today'), items: [] },
    { label: msg('Yesterday'), items: [] },
    { label: msg('Earlier'), items: [] },
  ]
  for (const item of items) {
    const day = dayOf(new Date(item.createdAtUtc))
    groups[day === today ? 0 : day === yesterday ? 1 : 2]!.items.push(item)
  }
  return groups.filter((group) => group.items.length > 0)
}

/** The ones that arrived since the person last looked (newer than the newest they had seen), oldest first. */
export function arrivedSince(items: Notification[], seenUpToId: number): Notification[] {
  return items.filter((item) => item.id > seenUpToId).sort((a, b) => a.id - b.id)
}
