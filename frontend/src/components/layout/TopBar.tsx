import { Search } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { Link } from 'react-router'
import { ThemeToggle } from '@/components/fx/ThemeToggle'
import { TopProgress } from '@/components/fx/TopProgress'
import { AssistantLauncher } from '@/features/assistant/AssistantLauncher'
import { useBrandName } from '@/features/branding/api'
import { NotificationBell } from '@/features/notifications/NotificationBell'
import type { LayoutChoice } from '@/lib/preferences'
import { BrandMark } from './BrandMark'
import { TopMegaNav } from './TopMegaNav'
import { useNav } from './use-nav'
import { UserMenu } from './UserMenu'

/**
 * The frosted bar across the top of every layout: the page title (or, in the top-menus layout, the brand and the
 * menus), the search palette, light/dark, notifications, the assistant and the account menu.
 */
export function TopBar({ layout, onSearch, onShortcuts }: { layout: LayoutChoice; onSearch: () => void; onShortcuts: () => void }) {
  const { active } = useNav()
  const name = useBrandName()
  const title = active?.title ?? name
  const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform)

  return (
    <header className="fx-glass sticky top-0 z-30 flex h-14 shrink-0 items-center gap-2 border-b px-3 md:px-4">
      <TopProgress />
      {/* Phones: the menu is in the bottom bar, so the top bar starts with the company's mark. */}
      <Link to="/" className="shrink-0 md:hidden" aria-label={`${name} home`}>
        <BrandMark className="size-8" />
      </Link>

      {layout === 'top' && (
        <Link to="/" className="mr-2 hidden max-w-56 items-center gap-2 font-semibold tracking-tight md:flex" aria-label={`${name} home`}>
          <BrandMark className="size-8" />
          <span className="truncate">{name}</span>
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
          data-tour="search"
          className="group/search mr-1 flex h-9 items-center gap-2 rounded-xl border bg-background/60 px-3 text-sm text-muted-foreground shadow-xs transition-[border-color,box-shadow,color] outline-none hover:border-brand/40 hover:text-foreground hover:shadow-[0_0_0_3px_var(--brand-glow)] focus-visible:ring-2 focus-visible:ring-ring"
          aria-label="Search (Ctrl+K)"
          aria-keyshortcuts="Control+K"
        >
          <Search className="size-4 transition-transform group-hover/search:scale-110" />
          <span className="hidden lg:inline">Search…</span>
          <kbd className="hidden rounded-md border bg-muted px-1.5 py-0.5 font-mono text-[10px] lg:inline">{isMac ? '⌘K' : 'Ctrl K'}</kbd>
        </button>
        <ThemeToggle />
        <NotificationBell />
        <AssistantLauncher />
        <UserMenu onShortcuts={onShortcuts} />
      </div>
    </header>
  )
}
