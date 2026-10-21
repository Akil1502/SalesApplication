import { useLocation } from 'react-router'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { t } from '@/lib/i18n'
import { allowedNavGroups, findNavItem, type NavGroup, type NavItem } from './navigation'

/** A menu item as shown: in the language on screen, with the English kept for searching. */
export interface ShownNavItem extends NavItem {
  /** The English title and description, so the search palette finds a page by either language. */
  english: string
}

export interface ShownNavGroup extends Omit<NavGroup, 'items'> {
  items: ShownNavItem[]
}

function shown(item: NavItem): ShownNavItem {
  return { ...item, title: t(item.title), description: t(item.description), english: `${item.title} ${item.description}` }
}

/** The menu groups this user may open, and the item for the current page, translated. Shared by every layout. */
export function useNav(): { groups: ShownNavGroup[]; active: ShownNavItem | undefined } {
  const { can } = useAuth()
  const ai = useAiStatus()
  const { pathname } = useLocation()
  const active = findNavItem(pathname)
  return {
    groups: allowedNavGroups(can, ai.data?.enabled ?? false).map((group) => ({ ...group, label: t(group.label), items: group.items.map(shown) })),
    active: active && shown(active),
  }
}
