import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { NavLink } from 'react-router'
import { cn } from '@/lib/utils'
import { BrandMark } from './BrandMark'
import { useNav } from './use-nav'

export const RAIL_WIDTH = 72
const OPEN_WIDTH = 264
const spring = { type: 'spring', stiffness: 380, damping: 34 } as const

/**
 * A slim frosted rail of icons on the left that slides open with labels while the pointer (or keyboard focus) is on
 * it. It floats over the page when open, so the page never jumps. The lit pill glides to the current page.
 */
export function RailNav() {
  const { groups, active } = useNav()
  const [open, setOpen] = useState(false)

  return (
    <motion.nav
      aria-label="Main menu"
      className="fixed inset-y-0 left-0 z-40 hidden p-2 md:block"
      initial={{ x: -96, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: -96, opacity: 0 }}
      transition={spring}
      onPointerEnter={() => setOpen(true)}
      onPointerLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false)
      }}
    >
      <motion.div
        className="flex h-full flex-col overflow-hidden rounded-2xl border border-sidebar-border bg-sidebar text-sidebar-foreground shadow-[0_20px_50px_-20px_oklch(0.2_0.08_268/60%)]"
        animate={{ width: open ? OPEN_WIDTH : RAIL_WIDTH - 16 }}
        initial={false}
        transition={spring}
      >
        <NavLink to="/" className="flex h-14 shrink-0 items-center gap-3 px-2.5" aria-label="SalesApp home">
          <BrandMark />
          <RailLabel open={open} className="font-semibold tracking-tight">
            SalesApp
            <span className="block text-xs font-normal text-sidebar-foreground/60">Sales &amp; GST billing</span>
          </RailLabel>
        </NavLink>

        <div className="flex-1 overflow-x-hidden overflow-y-auto px-2 pb-3 [scrollbar-width:none]">
          {groups.map((group, index) => (
            <div key={group.label} className="pt-3">
              <div className="relative h-5">
                <AnimatePresence initial={false}>
                  {open ? (
                    <motion.p
                      key="label"
                      className="absolute inset-x-2 text-[11px] font-medium tracking-wide text-sidebar-foreground/50 uppercase"
                      initial={{ opacity: 0, x: -8 }}
                      animate={{ opacity: 1, x: 0 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: 0.15 }}
                    >
                      {group.label}
                    </motion.p>
                  ) : (
                    index > 0 && (
                      <motion.span key="rule" aria-hidden className="absolute inset-x-3 top-2 h-px bg-sidebar-foreground/12" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} />
                    )
                  )}
                </AnimatePresence>
              </div>
              <ul className="space-y-0.5">
                {group.items.map((item) => {
                  const current = active?.to === item.to
                  return (
                    <li key={item.to}>
                      <NavLink
                        to={item.to}
                        end
                        title={open ? undefined : item.title}
                        aria-current={current ? 'page' : undefined}
                        className={cn(
                          'group/item relative isolate flex h-10 items-center gap-3 rounded-xl px-3 text-sm outline-none',
                          'focus-visible:ring-2 focus-visible:ring-sidebar-ring',
                          current ? 'text-sidebar-accent-foreground' : 'text-sidebar-foreground/75 hover:text-sidebar-foreground',
                        )}
                      >
                        {current && (
                          <motion.span
                            layoutId="rail-pill"
                            aria-hidden
                            className="absolute inset-0 -z-10 rounded-xl"
                            style={{
                              background: 'linear-gradient(90deg, oklch(0.6 0.16 268 / 45%), oklch(0.6 0.16 268 / 10%))',
                              boxShadow: 'inset 2px 0 0 var(--sidebar-primary), 0 6px 18px -8px var(--brand-glow)',
                            }}
                            transition={spring}
                          />
                        )}
                        <span aria-hidden className="absolute inset-0 -z-20 rounded-xl bg-sidebar-accent opacity-0 transition-opacity group-hover/item:opacity-60" />
                        <item.icon className="size-[18px] shrink-0 transition-transform duration-300 ease-[var(--ease-spring)] group-hover/item:scale-115 group-hover/item:-rotate-6" />
                        <RailLabel open={open}>{item.title}</RailLabel>
                      </NavLink>
                    </li>
                  )
                })}
              </ul>
            </div>
          ))}
        </div>
      </motion.div>
    </motion.nav>
  )
}

/** Text that slides in beside the icons as the rail opens. */
function RailLabel({ open, className, children }: { open: boolean; className?: string; children: React.ReactNode }) {
  return (
    <motion.span
      className={cn('min-w-0 truncate leading-tight whitespace-nowrap', className)}
      animate={{ opacity: open ? 1 : 0, x: open ? 0 : -10 }}
      initial={false}
      transition={{ duration: 0.2 }}
      aria-hidden={!open}
    >
      {children}
    </motion.span>
  )
}
