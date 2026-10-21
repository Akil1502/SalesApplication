import { useEffect, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useAuth } from '@/features/auth/auth-context'
import { isTyping, overlayOpen, plainKey } from '@/lib/keyboard'
import { panelListOf } from './record-panel-state'
import { GO_TO_PAGES, NEW_RECORD_PAGES } from './shortcuts'
import { useNav } from './use-nav'

/**
 * App-wide keys: / searches the page's list, N starts a new record on a list, G then a letter jumps to a page, ? shows
 * the shortcut sheet. Nothing fires while typing in a field or while a dialog or menu is open.
 */
export function useGlobalShortcuts({ onSearch, onHelp }: { onSearch: () => void; onHelp: () => void }) {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { can } = useAuth()
  const { groups } = useNav()
  const pendingGo = useRef(0)

  useEffect(() => {
    const allowed = new Set(groups.flatMap((group) => group.items.map((item) => item.to)))

    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || !plainKey(event) || isTyping(event) || overlayOpen()) return
      const key = event.key.toLowerCase()

      // The second key of "G then a letter".
      if (pendingGo.current > performance.now()) {
        pendingGo.current = 0
        const page = GO_TO_PAGES.find((p) => p.key === key)
        if (page && allowed.has(page.to)) {
          event.preventDefault()
          navigate(page.to)
        }
        return
      }

      if (event.key === '?') {
        event.preventDefault()
        onHelp()
      } else if (key === 'g') {
        pendingGo.current = performance.now() + 1200
      } else if (event.key === '/') {
        event.preventDefault()
        const search = document.querySelector<HTMLInputElement>('main [data-page-search]')
        if (search) search.focus()
        else onSearch()
      } else if (key === 'n') {
        const list = panelListOf(pathname)
        const page = NEW_RECORD_PAGES.find((p) => p.list === list && pathname === list)
        if (page && can(page.permission)) {
          event.preventDefault()
          navigate(page.to)
        }
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [groups, navigate, pathname, can, onSearch, onHelp])
}
