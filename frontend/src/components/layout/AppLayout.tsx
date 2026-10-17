import { AnimatePresence, motion } from 'motion/react'
import { useCallback, useState } from 'react'
import { Outlet, ScrollRestoration } from 'react-router'
import { ConnectionNotice } from '@/components/errors/ConnectionNotice'
import { Backdrop } from '@/components/fx/Backdrop'
import { PageTransition } from '@/components/fx/PageTransition'
import { useMediaQuery, usePreferences } from '@/components/fx/preferences-context'
import { useCursorEffects } from '@/components/fx/use-cursor-effects'
import { useScrollReveal } from '@/components/fx/use-scroll-reveal'
import { cn } from '@/lib/utils'
import { CommandPalette } from './CommandPalette'
import { DockNav } from './DockNav'
import { RAIL_OPEN_WIDTH, RAIL_WIDTH, railSpring } from './rail'
import { RailNav } from './RailNav'
import { scrollKey } from './record-panel-state'
import { ShortcutsDialog } from './ShortcutsDialog'
import { TopBar } from './TopBar'
import { useGlobalShortcuts } from './use-global-shortcuts'

/**
 * Signed-in shell. The menus take the layout each user picked (an icon rail, menus across the top, or a dock at the
 * bottom; phones always get a slide-in menu), with a frosted top bar, Ctrl+K search, and pages that swing into
 * place over a slowly moving backdrop.
 */
export function AppLayout() {
  const { layout } = usePreferences()
  const [searchOpen, setSearchOpen] = useState(false)
  const [shortcutsOpen, setShortcutsOpen] = useState(false)
  const [railOpen, setRailOpen] = useState(false)
  const desktop = useMediaQuery('(min-width: 768px)')
  // The page makes room for the rail, and moves over (with the same spring) while the rail is open.
  const railRoom = layout === 'rail' && desktop ? (railOpen ? RAIL_OPEN_WIDTH : RAIL_WIDTH) : 0
  useScrollReveal()
  useCursorEffects()
  const openSearch = useCallback(() => setSearchOpen(true), [])
  const openShortcuts = useCallback(() => setShortcutsOpen(true), [])
  useGlobalShortcuts({ onSearch: openSearch, onHelp: openShortcuts })

  return (
    <div className="relative isolate min-h-svh">
      {/* For keyboard and screen-reader users: straight past the menus to the page. */}
      <a
        href="#main"
        className="fixed top-2 left-2 z-[60] -translate-y-20 rounded-lg bg-primary px-3 py-2 text-sm font-medium text-primary-foreground shadow-lg transition-transform focus-visible:translate-y-0"
      >
        Skip to the page
      </a>
      {/* A new page opens at the top; going back returns to where you were; a record panel never moves its list. */}
      <ScrollRestoration getKey={scrollKey} />
      <Backdrop />

      <AnimatePresence>
        {layout === 'rail' && <RailNav key="rail" open={railOpen} onOpenChange={setRailOpen} />}
        {layout === 'dock' && <DockNav key="dock" />}
      </AnimatePresence>

      <motion.div
        className={cn('flex min-h-svh min-w-0 flex-col', layout === 'dock' && 'md:pb-24')}
        initial={false}
        animate={{ paddingLeft: railRoom }}
        transition={railSpring}
      >
        <TopBar layout={layout} onSearch={openSearch} onShortcuts={openShortcuts} />
        <ConnectionNotice />
        <main id="main" tabIndex={-1} className="@container/page flex min-w-0 flex-1 flex-col p-4 outline-none md:p-6">
          <PageTransition>
            <Outlet />
          </PageTransition>
        </main>
      </motion.div>

      <CommandPalette open={searchOpen} onOpenChange={setSearchOpen} />
      <ShortcutsDialog open={shortcutsOpen} onOpenChange={setShortcutsOpen} />
    </div>
  )
}
