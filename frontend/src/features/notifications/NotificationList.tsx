import { BellOff } from 'lucide-react'
import { t } from '@/lib/i18n'
import type { Notification } from './api'
import { groupByDay, KIND_LOOK, whenText } from './format'

interface NotificationListProps {
  items: Notification[]
  /** Items newer than this arrived since the person last opened the bell. */
  newAfter?: number
  onOpen: (item: Notification) => void
  emptyText: string
}

function NotificationRow({ item, isNew, onOpen }: { item: Notification; isNew: boolean; onOpen: (item: Notification) => void }) {
  const look = KIND_LOOK[item.kind]
  return (
    <li>
      <button
        type="button"
        onClick={() => onOpen(item)}
        className="group flex w-full items-start gap-3 rounded-lg px-3 py-2.5 text-left transition-colors hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
      >
        <span className={`mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg ${look.tile}`}>
          <look.icon className="size-4" aria-hidden />
        </span>
        <span className="min-w-0 flex-1">
          <span className={`block text-sm leading-snug ${item.read ? 'text-foreground/80' : 'font-semibold'}`}>{item.title}</span>
          {item.body && <span className="mt-0.5 line-clamp-2 block text-xs text-muted-foreground">{item.body}</span>}
          <span className="mt-1 flex items-center gap-2 text-[11px] text-muted-foreground">
            {whenText(item.createdAtUtc)}
            {isNew && <span className="rounded-full bg-primary/12 px-1.5 py-px font-medium text-primary">{t('New since your last visit')}</span>}
          </span>
        </span>
        {!item.read && <span className="mt-2 size-2 shrink-0 rounded-full bg-primary" aria-label={t('Unread')} role="img" />}
      </button>
    </li>
  )
}

/** Notifications under Today, Yesterday and Earlier; unread ones in bold with a dot. */
export function NotificationList({ items, newAfter = Number.MAX_SAFE_INTEGER, onOpen, emptyText }: NotificationListProps) {
  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 px-6 py-10 text-center text-sm text-muted-foreground">
        <BellOff className="size-6 opacity-60" aria-hidden />
        {emptyText}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      {groupByDay(items).map((group) => (
        <section key={group.label} aria-label={t(group.label)}>
          <h3 className="px-3 pb-1 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">{t(group.label)}</h3>
          <ul className="flex flex-col">
            {group.items.map((item) => (
              <NotificationRow key={item.id} item={item} isNew={item.id > newAfter} onOpen={onOpen} />
            ))}
          </ul>
        </section>
      ))}
    </div>
  )
}
