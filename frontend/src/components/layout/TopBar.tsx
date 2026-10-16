import { Search } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { Link } from 'react-router'
import { ThemeToggle } from '@/components/fx/ThemeToggle'
import { TopProgress } from '@/components/fx/TopProgress'
import { AssistantLauncher } from '@/features/assistant/AssistantLauncher'
import type { LayoutChoice } from '@/lib/preferences'
import { BrandMark } from './BrandMark'
import { MobileNav } from './MobileNav'
import { TopMegaNav } from './TopMegaNav'
import { useNav } from './use-nav'
import { UserMenu } from './UserMenu'

/**
 * The frosted bar across the top of every layout: the page title (or, in the top-menus layout, the brand and the
 * menus), the search palette, light/dark, the assistant and the account menu.
 */
export function TopBar({ layout, onSearch }: { layout: LayoutChoice; onSearch: () => void }) {
  const { active } = useNav()
  const title = active?.title ?? 'SalesApp'
  const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform)

  return (
    <header className="fx-glass sticky top-0 z-30 flex h-14 shrink-0 items-center gap-2 border-b px-3 md:px-4">
      <TopProgress />
      <MobileNav />

      {layout === 'top' && (
        <Link to="/" className="mr-2 hidden items-center gap-2 font-semibold tracking-tight md:flex" aria-label="SalesApp home">
          <BrandMark className="size-8" />
          SalesApp
        </Link>
      )}

      {layout === 'top' ? (
        <>
          <TopMegaNav />
          <span className="text-sm font-medium md:hidden">{title}</span>
        </>
      ) : (
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
      )}

      <div className="ml-auto flex items-center gap-1">
        <button
          type="button"
          onClick={onSearch}
          className="group/search mr-1 flex h-9 items-center gap-2 rounded-xl border bg-background/60 px-3 text-sm text-muted-foreground shadow-xs transition-[border-color,box-shadow,color] outline-none hover:border-brand/40 hover:text-foreground hover:shadow-[0_0_0_3px_var(--brand-glow)] focus-visible:ring-2 focus-visible:ring-ring"
          aria-label="Search (Ctrl+K)"
        >
          <Search className="size-4 transition-transform group-hover/search:scale-110" />
          <span className="hidden lg:inline">Search…</span>
          <kbd className="hidden rounded-md border bg-muted px-1.5 py-0.5 font-mono text-[10px] lg:inline">{isMac ? '⌘K' : 'Ctrl K'}</kbd>
        </button>
        <ThemeToggle />
        <AssistantLauncher />
        <UserMenu />
      </div>
    </header>
  )
}
