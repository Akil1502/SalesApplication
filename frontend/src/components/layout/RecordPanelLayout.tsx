import { ChevronRight, Maximize2, Minimize2, X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useLocation, useNavigate, useOutlet } from 'react-router'
import { useMotionLevel, usePreferences } from '@/components/fx/preferences-context'
import { Button } from '@/components/ui/button'
import { findNavItem } from './navigation'
import { RAIL_WIDTH } from './rail'
import { InPanelContext, OpenRecordContext, recentMorphOrigin, setMorphOrigin } from './record-panel-state'

const spring = { type: 'spring', stiffness: 320, damping: 34 } as const
const GAP = 8

/** True while a dialog, menu or popover of the page is open: Escape belongs to it, not to the panel. */
function somethingElseIsOpen() {
  return !!document.querySelector('[role="dialog"][data-state="open"], [role="menu"], [role="listbox"], [data-radix-popper-content-wrapper]')
}

function Panel({
  listTitle,
  onClose,
  children,
}: {
  listTitle: string
  onClose: () => void
  children: ReactNode
}) {
  const { pathname } = useLocation()
  const { layout } = usePreferences()
  const level = useMotionLevel()
  const [expanded, setExpanded] = useState(false)
  const [origin] = useState(recentMorphOrigin)
  const panel = useRef<HTMLElement>(null)
  const left = layout === 'rail' && window.innerWidth >= 768 ? RAIL_WIDTH + GAP : GAP
  const width = expanded ? `calc(100vw - ${left + GAP}px)` : `min(54rem, calc(100vw - ${left + GAP * 2}px))`

  // Focus moves into the panel as it opens; the row it grew from is used up.
  useEffect(() => {
    panel.current?.focus({ preventScroll: true })
    setMorphOrigin(null)
  }, [])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && !somethingElseIsOpen()) onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // The row the user clicked swells into the panel: a ghost grows from the row's place to the panel's, and the
  // panel fades in over it.
  const morph = origin && level === 'full'
  const target = { top: GAP, height: window.innerHeight - GAP * 2, width: Math.min(864, window.innerWidth - left - GAP * 2) }

  return (
    <>
      <motion.div
        aria-hidden
        className="fixed inset-0 z-40 bg-[oklch(0.2_0.05_268/18%)] backdrop-blur-[2px]"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      />
      {morph && (
        <motion.div
          aria-hidden
          className="pointer-events-none fixed z-50 rounded-2xl border border-brand/40"
          style={{ background: 'linear-gradient(135deg, color-mix(in oklch, var(--brand) 18%, var(--card)), var(--card))' }}
          initial={{ top: origin.top, left: origin.left, width: origin.width, height: origin.height, opacity: 1, borderRadius: 8 }}
          animate={{ top: target.top, left: window.innerWidth - GAP - target.width, width: target.width, height: target.height, opacity: 0, borderRadius: 16 }}
          transition={{ duration: 0.55, ease: [0.16, 1, 0.3, 1], opacity: { duration: 0.25, delay: 0.35 } }}
        />
      )}
      <motion.aside
        ref={panel}
        tabIndex={-1}
        aria-label={`${listTitle}: record`}
        className="fixed inset-0 z-50 flex flex-col overflow-hidden bg-background shadow-[0_30px_80px_-20px_oklch(0.2_0.08_268/55%)] outline-none md:inset-auto md:top-2 md:right-2 md:bottom-2 md:w-(--panel-width) md:rounded-2xl md:border md:transition-[width] md:duration-500 md:ease-[var(--ease-out)]"
        style={{ '--panel-width': width } as React.CSSProperties}
        initial={morph ? { opacity: 0, x: 24 } : { x: '105%' }}
        animate={{ opacity: 1, x: 0 }}
        exit={{ x: '105%', transition: { duration: 0.3, ease: [0.4, 0, 1, 1] } }}
        transition={morph ? { delay: 0.18, duration: 0.4, ease: [0.16, 1, 0.3, 1] } : spring}
      >
        <div className="flex h-12 shrink-0 items-center gap-1 border-b px-2">
          <Button variant="ghost" size="icon-sm" onClick={onClose} aria-label="Close">
            <X />
          </Button>
          <span className="flex items-center gap-1 text-sm text-muted-foreground">
            {listTitle} <ChevronRight className="size-3.5" />
          </span>
          <Button
            variant="ghost"
            size="sm"
            className="ml-auto hidden md:inline-flex"
            onClick={() => setExpanded((on) => !on)}
            aria-pressed={expanded}
          >
            {expanded ? <Minimize2 /> : <Maximize2 />}
            {expanded ? 'Side panel' : 'Full width'}
          </Button>
        </div>
        <div className="flex-1 overflow-y-auto">
          <AnimatePresence mode="popLayout" initial={false}>
            <motion.div
              key={pathname}
              className="flex min-h-full flex-col gap-4 p-4 md:p-6"
              initial={{ opacity: 0, y: 16 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -10 }}
              transition={{ duration: 0.3, ease: [0.16, 1, 0.3, 1] }}
            >
              <InPanelContext value={true}>{children}</InPanelContext>
            </motion.div>
          </AnimatePresence>
        </div>
      </motion.aside>
    </>
  )
}

/**
 * A list whose records open in a slide-over panel beside it rather than replacing it. The record's route is a child
 * of the list's, so its address can still be shared or bookmarked (the list opens behind it), Back closes it, and
 * the list keeps its search and filters while the panel is open.
 */
export function RecordPanelLayout({ list, children }: { list: string; children: ReactNode }) {
  const outlet = useOutlet()
  const location = useLocation()
  const navigate = useNavigate()
  const open = outlet !== null && location.pathname !== list
  const openId = open ? location.pathname.slice(list.length + 1).split('/')[0] : undefined
  const listSearch = useRef(location.search)

  // Remember the list's search while it's on its own, and carry it into a record opened without one, so the list
  // behind the panel keeps its filters and page.
  useEffect(() => {
    if (!open) listSearch.current = location.search
    else if (!location.search && listSearch.current) navigate({ pathname: location.pathname, search: listSearch.current }, { replace: true })
  }, [open, location.pathname, location.search, navigate])

  const close = useCallback(() => navigate({ pathname: list, search: listSearch.current }), [navigate, list])

  return (
    <>
      <OpenRecordContext value={openId}>{children}</OpenRecordContext>
      {/* In the body, so the page's swing-in transform can't pin the panel inside the page. */}
      {createPortal(
        <AnimatePresence>
          {open && (
            <Panel key="record-panel" listTitle={findNavItem(list)?.title ?? 'Back'} onClose={close}>
              {outlet}
            </Panel>
          )}
        </AnimatePresence>,
        document.body,
      )}
    </>
  )
}
