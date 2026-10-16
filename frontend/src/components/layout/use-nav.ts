import { useLocation } from 'react-router'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { allowedNavGroups, findNavItem } from './navigation'

/** The menu groups this user may open, and the item for the current page. Shared by every layout. */
export function useNav() {
  const { can } = useAuth()
  const ai = useAiStatus()
  const { pathname } = useLocation()
  return { groups: allowedNavGroups(can, ai.data?.enabled ?? false), active: findNavItem(pathname) }
}
