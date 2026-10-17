import { AnimatePresence, motion, useMotionValue, useSpring, useTransform, type MotionValue } from 'motion/react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link, NavLink, useLocation } from 'react-router'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { cn } from '@/lib/utils'
import type { NavGroup } from './navigation'
import { useNav } from './use-nav'

const spring = { type: 'spring', stiffness: 400, damping: 32 } as const

/** One dock icon: it grows as the pointer comes near, like the macOS dock. */
function DockIcon({ mouseX, magnify, label, active, children }: { mouseX: MotionValue<number>; magnify: boolean; label: string; active: boolean; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  const distance = useTransform(mouseX, (x) => {
    const box = ref.current?.getBoundingClientRect()
    return box ? x - box.left - box.width / 2 : Infinity
  })
  const target = useTransform(distance, [-150, 0, 150], [46, magnify ? 70 : 46, 46])
  const size = useSpring(target, { mass: 0.12, stiffness: 200, damping: 15 })

  return (
    <motion.div ref={ref} className="group/dock relative flex items-end justify-center" style={{ width: size, height: size }}>
      {children}
      <span className="pointer-events-none absolute -top-9 left-1/2 -translate-x-1/2 scale-90 rounded-md bg-popover px-2 py-1 text-xs font-medium whitespace-nowrap text-popover-foreground opacity-0 shadow-md ring-1 ring-foreground/10 transition-[opacity,scale] group-hover/dock:scale-100 group-hover/dock:opacity-100">
        {label}
      </span>
      {active && <motion.span layoutId="dock-dot" aria-hidden className="absolute -bottom-2 size-1.5 rounded-full bg-brand shadow-[0_0_8px_var(--brand)]" transition={spring} />}
    </motion.div>
  )
}

const tileClass =
  'flex size-full items-center justify-center rounded-[28%] text-white shadow-[0_8px_20px_-8px_var(--brand-glow)] outline-none ring-1 ring-white/20 transition-[filter] hover:brightness-110 focus-visible:ring-2 focus-visible:ring-ring'

/** The pages of a group, popping up above the dock. */
function GroupPanel({ group, activePath }: { group: NavGroup; activePath: string | undefined }) {
  return (
    <motion.div
      className="absolute bottom-full left-1/2 mb-4 bg-popover/95 backdrop-blur-xl w-80 -translate-x-1/2 rounded-2xl border p-2 shadow-[var(--shadow-lifted)]"
      initial={{ opacity: 0, y: 16, scale: 0.9 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: 12, scale: 0.94, transition: { duration: 0.15 } }}
      transition={spring}
      style={{ transformOrigin: 'bottom center' }}
    >
      <p className="px-2 pt-1 pb-2 text-xs font-medium tracking-wide text-muted-foreground uppercase">{group.label}</p>
      <ul>
        {group.items.map((item, index) => (
          <motion.li key={item.to} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.03 * index }}>
            <Link
              to={item.to}
              className={cn(
                'flex items-center gap-3 rounded-xl p-2 transition-colors outline-none hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring',
                activePath === item.to && 'bg-accent/70',
              )}
            >
              <span className="flex size-8 shrink-0 items-center justify-center rounded-lg text-white" style={{ background: 'var(--gradient-brand)' }}>
                <item.icon className="size-4" />
              </span>
              <span className="min-w-0">
                <span className="block text-sm font-medium">{item.title}</span>
                <span className="block truncate text-xs text-muted-foreground">{item.description}</span>
              </span>
            </Link>
          </motion.li>
        ))}
      </ul>
    </motion.div>
  )
}

/**
 * A frosted dock floating at the bottom. Its icons grow as the pointer passes over them; a group's icon pops up
 * its pages, and a glowing dot sits under the group you're in.
 */
export function DockNav() {
  const { groups, active } = useNav()
  const { pathname } = useLocation()
  const magnify = useMotionLevel() === 'full'
  const mouseX = useMotionValue(Infinity)
  const [open, setOpen] = useState<string | null>(null)
  const dock = useRef<HTMLElement>(null)

  // Moving to another page closes the panel.
  const [shownFor, setShownFor] = useState(pathname)
  if (shownFor !== pathname) {
    setShownFor(pathname)
    setOpen(null)
  }

  useEffect(() => {
    if (!open) return
    const onPointer = (event: PointerEvent) => {
      if (!dock.current?.contains(event.target as Node)) setOpen(null)
    }
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(null)
    document.addEventListener('pointerdown', onPointer)
    window.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      window.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <motion.nav
      ref={dock}
      aria-label="Main menu"
      className="fixed inset-x-0 bottom-4 z-40 hidden justify-center md:flex"
      initial={{ y: 120, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      exit={{ y: 120, opacity: 0 }}
      transition={spring}
    >
      <div
        data-tour="nav"
        className="fx-glass flex h-[66px] items-end gap-2.5 rounded-[22px] border px-3 pb-2.5 shadow-[0_24px_60px_-20px_oklch(0.2_0.08_268/55%)]"
        onPointerMove={(event) => mouseX.set(event.clientX)}
        onPointerLeave={() => mouseX.set(Infinity)}
      >
        {groups.map((group) => {
          const single = group.items.length === 1 ? group.items[0] : undefined
          const isActive = group.items.some((item) => item.to === active?.to)
          return (
            <div key={group.label} className="relative">
              <DockIcon mouseX={mouseX} magnify={magnify} label={single?.title ?? group.label} active={isActive}>
                {single ? (
                  <NavLink to={single.to} end aria-label={single.title} className={tileClass} style={{ background: 'var(--gradient-brand)' }}>
                    <group.icon className="size-[45%]" />
                  </NavLink>
                ) : (
                  <button
                    type="button"
                    aria-label={group.label}
                    aria-expanded={open === group.label}
                    className={tileClass}
                    style={{ background: 'var(--gradient-brand)' }}
                    onClick={() => setOpen(open === group.label ? null : group.label)}
                  >
                    <group.icon className="size-[45%]" />
                  </button>
                )}
              </DockIcon>
              <AnimatePresence>{open === group.label && <GroupPanel group={group} activePath={active?.to} />}</AnimatePresence>
            </div>
          )
        })}
      </div>
    </motion.nav>
  )
}
