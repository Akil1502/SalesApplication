import { AnimatePresence } from 'motion/react'
import { useState } from 'react'
import { Outlet, ScrollRestoration } from 'react-router'
import { Backdrop } from '@/components/fx/Backdrop'
import { PageTransition } from '@/components/fx/PageTransition'
import { usePreferences } from '@/components/fx/preferences-context'
import { useCursorEffects } from '@/components/fx/use-cursor-effects'
import { useScrollReveal } from '@/components/fx/use-scroll-reveal'
import { cn } from '@/lib/utils'
import { CommandPalette } from './CommandPalette'
import { DockNav } from './DockNav'
import { RailNav } from './RailNav'
import { scrollKey } from './record-panel-state'
import { TopBar } from './TopBar'

/**
 * Signed-in shell. The menus take the layout each user picked (an icon rail, menus across the top, or a dock at the
 * bottom; phones always get a slide-in menu), with a frosted top bar, Ctrl+K search, and pages that swing into
 * place over a slowly moving backdrop.
 */
export function AppLayout() {
  const { layout } = usePreferences()
  const [searchOpen, setSearchOpen] = useState(false)
  useScrollReveal()
  useCursorEffects()

  return (
    <div className="relative isolate min-h-svh">
      {/* A new page opens at the top; going back returns to where you were; a record panel never moves its list. */}
      <ScrollRestoration getKey={scrollKey} />
      <Backdrop />

      <AnimatePresence>
        {layout === 'rail' && <RailNav key="rail" />}
        {layout === 'dock' && <DockNav key="dock" />}
      </AnimatePresence>

      <div
        className={cn(
          'flex min-h-svh min-w-0 flex-col transition-[padding] duration-500 ease-[var(--ease-out)]',
          layout === 'rail' && 'md:pl-[72px]',
          layout === 'dock' && 'md:pb-24',
        )}
      >
        <TopBar layout={layout} onSearch={() => setSearchOpen(true)} />
        <main className="flex min-w-0 flex-1 flex-col p-4 md:p-6">
          <PageTransition>
            <Outlet />
          </PageTransition>
        </main>
      </div>

      <CommandPalette open={searchOpen} onOpenChange={setSearchOpen} />
    </div>
  )
}
