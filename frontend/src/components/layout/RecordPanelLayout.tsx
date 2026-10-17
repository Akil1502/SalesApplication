import { ChevronDown, ChevronRight, ChevronUp, Maximize2, Minimize2, X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useLocation, useNavigate, useOutlet } from 'react-router'
import { useMotionLevel, usePreferences } from '@/components/fx/preferences-context'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { isTyping, overlayOpen } from '@/lib/keyboard'
import { findNavItem } from './navigation'
import { RAIL_WIDTH } from './rail'
import { InPanelContext, ListRowsContext, OpenRecordContext, recentMorphOrigin, setMorphOrigin } from './record-panel-state'

const spring = { type: 'spring', stiffness: 320, damping: 34 } as const
const GAP = 8

/** The records either side of the open one in the list behind the panel, for stepping through them. */
interface Steps {
  position: number
  count: number
  previous?: string
  next?: string
}

function StepButton({ label, keys, disabled, onClick, children }: { label: string; keys: string; disabled: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button variant="ghost" size="icon-sm" onClick={onClick} disabled={disabled} aria-label={label} aria-keyshortcuts={keys}>
          {children}
        </Button>
      </TooltipTrigger>
      <TooltipContent>
        {label} <kbd className="ml-1 rounded border px-1 font-mono text-[10px]">{keys.split(' ')[0]}</kbd>
      </TooltipContent>
    </Tooltip>
  )
}

function Panel({
  listTitle,
  steps,
  onStep,
  onClose,
  children,
}: {
  listTitle: string
  steps: Steps | undefined
  onStep: (id: string) => void
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

  // Escape closes; J/K (or Alt+arrows) step to the next and previous record without closing.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || overlayOpen()) return
      if (event.key === 'Escape') return onClose()
      if (isTyping(event) || event.ctrlKey || event.metaKey) return
      const forward = event.key === 'j' || (event.altKey && event.key === 'ArrowDown')
      const back = event.key === 'k' || (event.altKey && event.key === 'ArrowUp')
      const target = forward ? steps?.next : back ? steps?.previous : undefined
      if (target) {
        event.preventDefault()
        onStep(target)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, onStep, steps])

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
          <Button variant="ghost" size="icon-sm" onClick={onClose} aria-label="Close" aria-keyshortcuts="Escape">
            <X />
          </Button>
          <span className="flex items-center gap-1 text-sm text-muted-foreground">
            {listTitle} <ChevronRight className="size-3.5" />
          </span>
          {steps && (
            <span className="ml-2 flex items-center gap-0.5">
              <StepButton label="Previous" keys="K Alt+ArrowUp" disabled={!steps.previous} onClick={() => steps.previous && onStep(steps.previous)}>
                <ChevronUp />
              </StepButton>
              <StepButton label="Next" keys="J Alt+ArrowDown" disabled={!steps.next} onClick={() => steps.next && onStep(steps.next)}>
                <ChevronDown />
              </StepButton>
              <span className="ml-1 text-xs text-muted-foreground tabular-nums" aria-live="polite">
                {steps.position} of {steps.count}
              </span>
            </span>
          )}
          <Button variant="ghost" size="sm" className="ml-auto hidden md:inline-flex" onClick={() => setExpanded((on) => !on)} aria-pressed={expanded}>
            {expanded ? <Minimize2 /> : <Maximize2 />}
            {expanded ? 'Side panel' : 'Full width'}
          </Button>
        </div>
        {/* A query container, so the page inside can rearrange to the panel's width (see the stacked tables). */}
        <div className="@container/page flex-1 overflow-y-auto">
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
 * the list keeps its search and filters while the panel is open. The list's table tells the panel its rows, so the
 * panel can step through them.
 */
export function RecordPanelLayout({ list, children }: { list: string; children: ReactNode }) {
  const outlet = useOutlet()
  const location = useLocation()
  const navigate = useNavigate()
  const open = outlet !== null && location.pathname !== list
  const openId = open ? location.pathname.slice(list.length + 1).split('/')[0] : undefined
  const listSearch = useRef(location.search)
  const lastOpen = useRef<string | undefined>(undefined)
  const [rowIds, setRowIds] = useState<string[]>([])

  // Remember the list's search while it's on its own, and carry it into a record opened without one, so the list
  // behind the panel keeps its filters and page.
  useEffect(() => {
    if (!open) listSearch.current = location.search
    else if (!location.search && listSearch.current) navigate({ pathname: location.pathname, search: listSearch.current }, { replace: true })
  }, [open, location.pathname, location.search, navigate])

  useEffect(() => {
    if (openId) lastOpen.current = openId
  }, [openId])

  const close = useCallback(() => {
    navigate({ pathname: list, search: listSearch.current })
    // Keyboard users carry on from the row of the record they were looking at.
    const id = lastOpen.current
    requestAnimationFrame(() => document.querySelector<HTMLElement>(`[data-row-key="${CSS.escape(id ?? '')}"]`)?.focus({ preventScroll: false }))
  }, [navigate, list])

  const step = useCallback((id: string) => navigate({ pathname: `${list}/${id}`, search: listSearch.current }), [navigate, list])

  const index = openId === undefined ? -1 : rowIds.indexOf(openId)
  const steps: Steps | undefined =
    index < 0 ? undefined : { position: index + 1, count: rowIds.length, previous: rowIds[index - 1], next: rowIds[index + 1] }

  return (
    <>
      <ListRowsContext value={setRowIds}>
        <OpenRecordContext value={openId}>{children}</OpenRecordContext>
      </ListRowsContext>
      {/* In the body, so the page's swing-in transform can't pin the panel inside the page. */}
      {createPortal(
        <AnimatePresence>
          {open && (
            <Panel key="record-panel" listTitle={findNavItem(list)?.title ?? 'Back'} steps={steps} onStep={step} onClose={close}>
              {outlet}
            </Panel>
          )}
        </AnimatePresence>,
        document.body,
      )}
    </>
  )
}
