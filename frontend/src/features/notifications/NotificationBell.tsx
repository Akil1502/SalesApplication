import { ArrowLeft, Bell, CheckCheck, Settings2 } from 'lucide-react'
import { motion } from 'motion/react'
import { createElement, useCallback, useEffect, useId, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { SegmentedControl } from '@/components/SegmentedControl'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { setFaviconDot } from '@/features/branding/apply'
import { useBranding } from '@/features/branding/api'
import { errorMessage } from '@/lib/api/errors'
import { setUnreadCount } from '@/lib/unread-count'
import { useMarkAllRead, useMarkRead, useNotificationPreferences, useNotifications, useOpened, useSaveNotificationPreferences, type Notification } from './api'
import { useDesktopNotifications } from './desktop'
import { arrivedSince, KIND_LOOK } from './format'
import { NotificationList } from './NotificationList'

type Desktop = ReturnType<typeof useDesktopNotifications>

/** Which kinds to receive, and Windows notifications for the important ones. */
function NotificationSettings({ desktop, onBack }: { desktop: Desktop; onBack: () => void }) {
  const preferences = useNotificationPreferences(true)
  const save = useSaveNotificationPreferences()
  const desktopId = useId()
  const kinds = preferences.data ?? []

  const toggle = (kind: Notification['kind'], receive: boolean) =>
    save.mutate(kinds.filter((k) => (k.kind === kind ? !receive : k.muted)).map((k) => k.kind), { onError: (error) => toast.error(errorMessage(error)) })

  return (
    <div className="flex max-h-[min(34rem,75vh)] flex-col">
      <div className="flex items-center gap-2 border-b px-2 py-2">
        <Button variant="ghost" size="icon" className="size-8" onClick={onBack} aria-label="Back to notifications">
          <ArrowLeft />
        </Button>
        <h2 className="text-sm font-semibold">Notification settings</h2>
      </div>
      <div className="flex flex-col gap-1 overflow-y-auto p-2">
        <p className="px-2 pb-1 text-xs text-muted-foreground">Choose what you hear about. You only ever get what your role lets you see.</p>
        {preferences.isPending
          ? Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="m-2 h-10" />)
          : kinds.map((k) => {
              const look = KIND_LOOK[k.kind]
              const id = `notify-${k.kind}`
              return (
                <div key={k.kind} className="flex items-center gap-3 rounded-lg px-2 py-2 hover:bg-accent/50">
                  <span className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${look.tile}`}>
                    <look.icon className="size-4" aria-hidden />
                  </span>
                  <label htmlFor={id} className="min-w-0 flex-1 cursor-pointer">
                    <span className="block text-sm font-medium">{k.label}</span>
                    <span className="block text-xs text-muted-foreground">{k.description}</span>
                  </label>
                  <Switch id={id} checked={!k.muted} onCheckedChange={(receive) => toggle(k.kind, receive)} disabled={save.isPending} />
                </div>
              )
            })}
        {desktop.permission !== 'unsupported' && (
          <div className="mt-1 border-t pt-2">
            <div className="flex items-center gap-3 rounded-lg px-2 py-2">
              <label htmlFor={desktopId} className="min-w-0 flex-1 cursor-pointer">
                <span className="block text-sm font-medium">Windows notifications</span>
                <span className="block text-xs text-muted-foreground">
                  Payments received and things waiting for your approval also pop up from Windows while this browser is in the background. Only on this
                  computer.
                </span>
              </label>
              <Switch id={desktopId} checked={desktop.on} onCheckedChange={(next) => void desktop.setOn(next)} disabled={desktop.permission === 'denied'} />
            </div>
            {desktop.permission === 'denied' && (
              <p className="px-2 pb-1 text-xs text-destructive">This browser has blocked notifications for this site. Allow them in the browser's site settings, then turn this on.</p>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

/**
 * The bell in the top bar: the unread count, a dropdown of the latest notifications, and a watch for new ones.
 * Important ones that arrive while the app is open pop up (or come from Windows when it's in the background, if
 * turned on); the tab's title and icon show the unread count.
 */
export function NotificationBell() {
  const navigate = useNavigate()
  const desktop = useDesktopNotifications()
  const notifications = useNotifications({ background: desktop.on })
  const markRead = useMarkRead()
  const markAllRead = useMarkAllRead()
  const opened = useOpened()
  const logoUrl = useBranding().data?.logoUrl
  const data = notifications.data
  const unread = data?.unreadCount ?? 0

  const [open, setOpen] = useState(false)
  const [settings, setSettings] = useState(false)
  const [tab, setTab] = useState<'all' | 'unread'>('all')
  const [visitMarker, setVisitMarker] = useState(Number.MAX_SAFE_INTEGER)

  // The bell rings when the count goes up (not when it goes down).
  const [shownCount, setShownCount] = useState(unread)
  const [rings, setRings] = useState(0)
  if (unread !== shownCount) {
    if (unread > shownCount) setRings(rings + 1)
    setShownCount(unread)
  }

  const openItem = useCallback(
    (item: Notification) => {
      if (!item.read) markRead.mutate(item.id)
      setOpen(false)
      navigate(item.link)
    },
    [markRead, navigate],
  )

  // Watch for new ones: the first load only sets the baseline; later arrivals that matter pop up.
  const seenUpTo = useRef<number | null>(null)
  useEffect(() => {
    if (!data) return
    setUnreadCount(data.unreadCount)
    setFaviconDot(data.unreadCount > 0)
    const newest = data.items[0]?.id ?? 0
    if (seenUpTo.current === null) {
      seenUpTo.current = newest
      return
    }

    const fresh = arrivedSince(data.items, seenUpTo.current).filter((item) => item.important && !item.read)
    seenUpTo.current = Math.max(seenUpTo.current, newest)
    for (const item of fresh.slice(-3)) {
      if (desktop.on && document.visibilityState === 'hidden') {
        const popup = new window.Notification(item.title, { body: item.body ?? undefined, icon: logoUrl ?? undefined, tag: `salesapp-${item.id}` })
        popup.onclick = () => {
          window.focus()
          openItem(item)
          popup.close()
        }
      } else {
        toast(item.title, {
          description: item.body ?? undefined,
          icon: createElement(KIND_LOOK[item.kind].icon, { className: 'size-4' }),
          action: { label: 'Open', onClick: () => openItem(item) },
        })
      }
    }
  }, [data, desktop.on, logoUrl, openItem])

  // Signing out takes the count off the tab.
  useEffect(
    () => () => {
      setUnreadCount(0)
      setFaviconDot(false)
    },
    [],
  )

  const onOpenChange = (next: boolean) => {
    setOpen(next)
    if (!next) {
      setSettings(false)
      return
    }
    if (data) {
      setVisitMarker(data.lastOpenedUpToId)
      const newest = data.items[0]?.id
      if (newest && newest > data.lastOpenedUpToId) opened.mutate(newest)
    }
  }

  const items = (data?.items ?? []).filter((item) => tab === 'all' || !item.read)

  return (
    <Popover open={open} onOpenChange={onOpenChange}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="icon" className="relative rounded-full" aria-label={unread ? `Notifications, ${unread} unread` : 'Notifications'} data-tour="notifications">
          <motion.span key={rings} className="flex" animate={rings ? { rotate: [0, -20, 16, -12, 8, -4, 0] } : undefined} transition={{ duration: 0.9 }} style={{ transformOrigin: '50% 10%' }}>
            <Bell />
          </motion.span>
          {unread > 0 && (
            <span className="absolute -top-0.5 -right-0.5 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-destructive px-1 text-[10px] leading-none font-semibold text-white ring-2 ring-background">
              {unread > 99 ? '99+' : unread}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" sideOffset={8} className="w-[min(25rem,calc(100vw-1rem))] overflow-hidden rounded-xl p-0">
        {settings ? (
          <NotificationSettings desktop={desktop} onBack={() => setSettings(false)} />
        ) : (
          <div className="flex max-h-[min(34rem,75vh)] flex-col">
            <div className="flex items-center gap-1 border-b px-3 py-2">
              <h2 className="mr-auto text-sm font-semibold">Notifications</h2>
              <Button variant="ghost" size="sm" disabled={unread === 0} onClick={() => data?.items[0] && markAllRead.mutate(data.items[0].id)}>
                <CheckCheck /> Mark all read
              </Button>
              <Button variant="ghost" size="icon" className="size-8" onClick={() => setSettings(true)} aria-label="Notification settings">
                <Settings2 />
              </Button>
            </div>
            <div className="px-3 pt-2 pb-1">
              <SegmentedControl
                label="Show"
                value={tab}
                onValueChange={setTab}
                options={[
                  { value: 'all', label: 'All' },
                  { value: 'unread', label: unread ? `Unread (${unread > 99 ? '99+' : unread})` : 'Unread' },
                ]}
              />
            </div>
            <div className="overflow-y-auto p-1 pb-2">
              {notifications.isPending ? (
                <div className="flex flex-col gap-2 p-2">
                  {Array.from({ length: 4 }, (_, i) => (
                    <Skeleton key={i} className="h-12 w-full" />
                  ))}
                </div>
              ) : (
                <NotificationList
                  items={items}
                  newAfter={visitMarker}
                  onOpen={openItem}
                  emptyText={tab === 'unread' ? "You're all caught up." : 'Nothing yet. Payments, orders and anything waiting for you will show up here.'}
                />
              )}
            </div>
          </div>
        )}
      </PopoverContent>
    </Popover>
  )
}
