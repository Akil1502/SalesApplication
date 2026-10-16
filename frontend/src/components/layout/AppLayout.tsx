import { AnimatePresence, motion } from 'motion/react'
import { Outlet, ScrollRestoration, useLocation } from 'react-router'
import { AuroraBackground } from '@/components/fx/AuroraBackground'
import { PageTransition } from '@/components/fx/PageTransition'
import { ThemeToggle } from '@/components/fx/ThemeToggle'
import { TopProgress } from '@/components/fx/TopProgress'
import { Separator } from '@/components/ui/separator'
import { SidebarInset, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { AssistantLauncher } from '@/features/assistant/AssistantLauncher'
import { AppSidebar } from './AppSidebar'
import { findNavItem } from './navigation'

function usePageTitle(): string {
  const { pathname } = useLocation()
  return findNavItem(pathname)?.title ?? 'SalesApp'
}

/**
 * Signed-in shell: collapsible sidebar, a frosted top bar with the page title, theme switch and assistant, and the
 * routed page, which swings into place as it opens over softly drifting light.
 */
export function AppLayout() {
  const title = usePageTitle()

  return (
    <SidebarProvider>
      {/* A new page opens at the top; going back returns to where you were. */}
      <ScrollRestoration />
      <AppSidebar />
      {/* min-w-0: a wide chart or table must not stop the page column shrinking, or the page scrolls sideways.
          isolate: the background light sits behind the page but in front of the page's own background colour. */}
      <SidebarInset className="isolate min-w-0">
        <AuroraBackground />
        <header className="fx-glass sticky top-0 z-30 flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <TopProgress />
          <SidebarTrigger className="-ml-1" />
          <Separator orientation="vertical" className="mr-2 data-[orientation=vertical]:h-4" />
          <AnimatePresence mode="wait" initial={false}>
            <motion.h1
              key={title}
              className="text-sm font-medium"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={{ duration: 0.2 }}
            >
              {title}
            </motion.h1>
          </AnimatePresence>
          <div className="ml-auto flex items-center gap-1">
            <ThemeToggle />
            <AssistantLauncher />
          </div>
        </header>
        <div className="flex flex-1 flex-col p-4 md:p-6">
          <PageTransition>
            <Outlet />
          </PageTransition>
        </div>
      </SidebarInset>
    </SidebarProvider>
  )
}
