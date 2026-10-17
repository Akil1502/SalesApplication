import { ChevronDown } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, useLocation } from 'react-router'
import { cn } from '@/lib/utils'
import { useNav } from './use-nav'

const spring = { type: 'spring', stiffness: 420, damping: 36 } as const

/**
 * Menus across the top bar. Pointing at a group (or clicking it) drops a large panel of its pages, each with an
 * icon and a line about it; an underline glides to the group you're in. Groups with one page are plain links.
 */
export function TopMegaNav() {
  const { groups, active } = useNav()
  const { pathname } = useLocation()
  const [open, setOpen] = useState<string | null>(null)
  const closeTimer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const activeGroup = groups.find((group) => group.items.some((item) => item.to === active?.to))?.label

  // Moving to another page closes the panel.
  const [shownFor, setShownFor] = useState(pathname)
  if (shownFor !== pathname) {
    setShownFor(pathname)
    setOpen(null)
  }

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(null)
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])
  useEffect(() => () => clearTimeout(closeTimer.current), [])

  const show = (label: string | null) => {
    clearTimeout(closeTimer.current)
    setOpen(label)
  }
  const hideSoon = () => {
    clearTimeout(closeTimer.current)
    closeTimer.current = setTimeout(() => setOpen(null), 160)
  }

  const panel = groups.find((group) => group.label === open)

  return (
    <nav aria-label="Main menu" data-tour="nav" className="relative hidden h-full items-center md:flex" onPointerLeave={hideSoon} onPointerEnter={() => clearTimeout(closeTimer.current)}>
      <ul className="flex h-full items-center gap-0.5">
        {groups.map((group) => {
          const single = group.items.length === 1 ? group.items[0] : undefined
          const isActive = activeGroup === group.label
          const content = (
            <>
              {single ? single.title : group.label}
              {!single && <ChevronDown className={cn('size-3.5 transition-transform duration-300', open === group.label && 'rotate-180')} />}
              {isActive && (
                <motion.span layoutId="top-underline" aria-hidden className="absolute inset-x-2 -bottom-px h-0.5 rounded-full" style={{ background: 'var(--gradient-brand)' }} transition={spring} />
              )}
            </>
          )
          const className = cn(
            'relative flex h-9 items-center gap-1 rounded-lg px-3 text-sm font-medium transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring',
            isActive || open === group.label ? 'text-foreground' : 'text-muted-foreground hover:text-foreground',
            open === group.label && 'bg-accent',
          )
          return (
            <li key={group.label} className="relative">
              {single ? (
                <NavLink to={single.to} end className={className} onPointerEnter={() => show(null)}>
                  {content}
                </NavLink>
              ) : (
                <button
                  type="button"
                  className={className}
                  aria-expanded={open === group.label}
                  onPointerEnter={() => show(group.label)}
                  onClick={() => show(open === group.label ? null : group.label)}
                >
                  {content}
                </button>
              )}
            </li>
          )
        })}
      </ul>

      <AnimatePresence>
        {panel && (
          <motion.div
            key="mega"
            layout
            className="absolute top-full left-0 z-50 mt-2 bg-popover/95 backdrop-blur-xl w-[min(46rem,calc(100vw-8rem))] overflow-hidden rounded-2xl border p-3 shadow-[var(--shadow-lifted)]"
            initial={{ opacity: 0, y: -10, rotateX: -12, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, rotateX: 0, scale: 1 }}
            exit={{ opacity: 0, y: -8, scale: 0.98, transition: { duration: 0.15 } }}
            transition={spring}
            style={{ transformPerspective: 1000, transformOrigin: 'top left' }}
          >
            <AnimatePresence mode="popLayout" initial={false}>
              <motion.ul
                key={panel.label}
                className="grid gap-1 sm:grid-cols-2"
                initial={{ opacity: 0, x: 16 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: -16 }}
                transition={{ duration: 0.2 }}
              >
                {panel.items.map((item, index) => (
                  <motion.li key={item.to} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.04, duration: 0.25 }}>
                    <Link
                      to={item.to}
                      className={cn(
                        'group/mega flex items-start gap-3 rounded-xl p-3 transition-colors outline-none hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring',
                        active?.to === item.to && 'bg-accent/70',
                      )}
                    >
                      <span
                        className="flex size-10 shrink-0 items-center justify-center rounded-xl text-white shadow-[0_6px_16px_-6px_var(--brand-glow)] transition-transform duration-300 group-hover/mega:scale-110 group-hover/mega:-rotate-3"
                        style={{ background: 'var(--gradient-brand)' }}
                      >
                        <item.icon className="size-[18px]" />
                      </span>
                      <span className="min-w-0">
                        <span className="block text-sm font-medium">{item.title}</span>
                        <span className="block text-xs text-muted-foreground">{item.description}</span>
                      </span>
                    </Link>
                  </motion.li>
                ))}
              </motion.ul>
            </AnimatePresence>
          </motion.div>
        )}
      </AnimatePresence>
    </nav>
  )
}
