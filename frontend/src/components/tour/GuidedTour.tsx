import { Compass } from 'lucide-react'
import { AnimatePresence, motion, type Transition } from 'motion/react'
import { useCallback, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useLocation, useNavigate } from 'react-router'
import { useMediaQuery, useMotionLevel, usePreferences } from '@/components/fx/preferences-context'
import { Button } from '@/components/ui/button'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { useBrandName } from '@/features/branding/api'
import { t } from '@/lib/i18n'
import { useUserSetting } from '@/lib/user-settings'
import { cn } from '@/lib/utils'
import { TourContext } from './tour-context'
import { TOUR_VERSION, tourSteps, type TourStep } from './tour-steps'

const NOT_SEEN = null
const GUTTER = 16

interface Box {
  top: number
  left: number
  width: number
  height: number
}

/** The element a step points at, if it's on screen (the dock on a phone, say, isn't). */
function targetOf(target: string | undefined): HTMLElement | undefined {
  if (!target) return undefined
  return Array.from(document.querySelectorAll<HTMLElement>(`[data-tour="${target}"]`)).find((element) => {
    const box = element.getBoundingClientRect()
    return box.width > 0 && box.height > 0
  })
}

const sameBox = (a: Box | null, b: Box | null) =>
  a === b || (!!a && !!b && Math.abs(a.top - b.top) < 0.5 && Math.abs(a.left - b.left) < 0.5 && Math.abs(a.width - b.width) < 0.5 && Math.abs(a.height - b.height) < 0.5)

/** Where the card goes: beside a tall target, otherwise below it, or above when there's no room below. */
function cardPlacement(box: Box | null, width: number): { style: React.CSSProperties; centred: boolean } {
  const vw = window.innerWidth
  const vh = window.innerHeight
  if (!box) return { style: { width }, centred: true }
  const left = Math.min(Math.max(GUTTER, box.left + box.width / 2 - width / 2), vw - width - GUTTER)
  if (box.height > vh * 0.5 && vw - (box.left + box.width) > width + GUTTER * 2) {
    return { style: { width, left: box.left + box.width + GUTTER, top: Math.min(Math.max(GUTTER, box.top + 24), vh - 280) }, centred: false }
  }
  if (vh - (box.top + box.height) > 240) return { style: { width, left, top: box.top + box.height + 14 }, centred: false }
  return { style: { width, left, bottom: vh - box.top + 14 }, centred: false }
}

/** The step's main button, focused as each step's card appears, so Enter goes on and screen readers read the step. */
function NextButton(props: React.ComponentProps<typeof Button>) {
  const button = useRef<HTMLButtonElement>(null)
  useEffect(() => button.current?.focus(), [])
  return <Button ref={button} size="sm" {...props} />
}

/** The tour on screen: the spotlight and the step's card. */
export function TourOverlay({ steps, onClose }: { steps: TourStep[]; onClose: () => void }) {
  const motionLevel = useMotionLevel()
  const [index, setIndex] = useState(0)
  const [box, setBox] = useState<Box | null>(null)
  const overlay = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const bodyId = useId()
  const step = steps[index]!
  const last = index === steps.length - 1

  const go = useCallback(
    (by: 1 | -1) => {
      let next = index + by
      // Skip steps whose element isn't on this screen.
      while (next >= 0 && next < steps.length && steps[next]!.target && !targetOf(steps[next]!.target)) next += by
      if (next >= steps.length) onClose()
      else if (next >= 0) setIndex(next)
    },
    [index, steps, onClose],
  )

  // Follow the step's element: bring it into view, then track it every frame (menus open, pages settle).
  const target = step.target
  useEffect(() => {
    const element = targetOf(target)
    element?.scrollIntoView({ block: 'center', behavior: motionLevel === 'full' ? 'smooth' : 'auto' })
    let frame = 0
    const track = () => {
      const r = element?.getBoundingClientRect()
      const next = r ? { top: r.top, left: r.left, width: r.width, height: r.height } : null
      setBox((current) => (sameBox(current, next) ? current : next))
      frame = requestAnimationFrame(track)
    }
    frame = requestAnimationFrame(track)
    return () => cancelAnimationFrame(frame)
  }, [target, index, motionLevel])

  // A modal: the app behind can't be clicked or tabbed into.
  useEffect(() => {
    const root = document.getElementById('root')
    root?.setAttribute('inert', '')
    return () => root?.removeAttribute('inert')
  }, [])

  // The keys work wherever the focus is (a menu that just closed may have taken it), and Tab brings it back.
  useEffect(() => {
    const onKey = (event: globalThis.KeyboardEvent) => {
      const inside = overlay.current?.contains(document.activeElement) ?? false
      if (event.key === 'Escape') onClose()
      else if (event.key === 'ArrowRight' || (event.key === 'Enter' && !inside)) go(1)
      else if (event.key === 'ArrowLeft') go(-1)
      else if (event.key === 'Tab' && !inside) overlay.current?.querySelector<HTMLElement>('[role=dialog] button:last-of-type')?.focus()
      else return
      event.preventDefault()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [go, onClose])

  const spring: Transition = motionLevel === 'full' ? { type: 'spring', stiffness: 260, damping: 30 } : { duration: motionLevel === 'off' ? 0 : 0.2 }
  const width = Math.min(360, window.innerWidth - GUTTER * 2)
  const placement = cardPlacement(box, width)
  const pad = 8
  const hole = box
    ? { top: box.top - pad, left: box.left - pad, width: box.width + pad * 2, height: box.height + pad * 2, borderRadius: 16 }
    : { top: window.innerHeight / 2, left: window.innerWidth / 2, width: 0, height: 0, borderRadius: 0 }

  return createPortal(
    <div ref={overlay} className="fixed inset-0 z-[80]">
      {/* The spotlight: a hole whose enormous shadow dims everything else, gliding from one element to the next. */}
      <motion.div
        aria-hidden
        className={cn('pointer-events-none fixed ring-2 ring-white/80', !box && 'ring-0')}
        style={{ boxShadow: '0 0 0 200vmax oklch(0.12 0.02 268 / 62%)' }}
        initial={false}
        animate={hole}
        transition={spring}
      />

      <AnimatePresence mode="wait">
        <motion.div
          key={step.id}
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          aria-describedby={bodyId}
          tabIndex={-1}
          className={cn(
            'fixed flex flex-col gap-3 rounded-2xl border bg-popover p-5 text-popover-foreground shadow-[var(--shadow-lifted)] outline-none',
            placement.centred && 'top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2',
          )}
          style={placement.style}
          initial={{ opacity: 0, y: 10, scale: 0.97 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          exit={{ opacity: 0, y: -6, scale: 0.98 }}
          transition={{ duration: motionLevel === 'off' ? 0 : 0.18 }}
        >
          <div className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
            <Compass className="size-4 text-primary" aria-hidden />
            {t('Step {step} of {count}', { step: index + 1, count: steps.length })}
          </div>
          <h2 id={titleId} className="text-lg leading-snug font-semibold tracking-tight">
            {step.title}
          </h2>
          <p id={bodyId} className="text-sm text-muted-foreground">
            {step.body}
          </p>
          <div className="flex items-center gap-1" aria-hidden>
            {steps.map((s, i) => (
              <span key={s.id} className={cn('h-1.5 rounded-full transition-all', i === index ? 'w-5 bg-primary' : 'w-1.5 bg-muted-foreground/30')} />
            ))}
          </div>
          <div className="mt-1 flex items-center gap-2">
            {!last && (
              <Button variant="ghost" size="sm" className="-ml-2 mr-auto" onClick={onClose}>
                {index === 0 ? t('Not now') : t('Skip tour')}
              </Button>
            )}
            {index > 0 && (
              <Button variant="outline" size="sm" className={cn(last && 'mr-auto')} onClick={() => go(-1)}>
                {t('Back')}
              </Button>
            )}
            <NextButton onClick={() => go(1)}>{index === 0 ? t('Show me around') : last ? t('Finish') : t('Next')}</NextButton>
          </div>
        </motion.div>
      </AnimatePresence>
    </div>,
    document.body,
  )
}

/**
 * The guided tour: it starts by itself the first time someone opens the dashboard (once per person, remembered on
 * the server), and "Take the tour" in the account menu runs it again. The steps follow their role, layout and screen.
 */
export function TourProvider({ children }: { children: ReactNode }) {
  const { user, can } = useAuth()
  const { layout } = usePreferences()
  const phone = !useMediaQuery('(min-width: 768px)')
  const ai = useAiStatus()
  const appName = useBrandName()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [seen, setSeen, loaded] = useUserSetting<{ done?: number } | null>('tour', NOT_SEEN)
  const [running, setRunning] = useState(false)

  const steps = useMemo(
    () => tourSteps({ appName, firstName: user?.fullName.split(' ')[0] ?? '', layout, phone, can, aiEnabled: ai.data?.enabled ?? false }),
    [appName, user, layout, phone, can, ai.data?.enabled],
  )

  // Once per person: the first time they're on the dashboard, after it has had a moment to appear.
  const due = loaded && !running && (seen?.done ?? 0) < TOUR_VERSION && pathname === '/'
  useEffect(() => {
    if (!due) return
    const timer = setTimeout(() => setRunning(true), 1400)
    return () => clearTimeout(timer)
  }, [due])

  const finish = useCallback(() => {
    setRunning(false)
    setSeen({ done: TOUR_VERSION })
  }, [setSeen])

  // Started from a menu: give it a moment to close (and the dashboard to appear) first.
  const start = useCallback(() => {
    if (pathname !== '/') navigate('/')
    setTimeout(() => setRunning(true), pathname === '/' ? 250 : 700)
  }, [pathname, navigate])

  const controls = useMemo(() => ({ start }), [start])

  return (
    <TourContext.Provider value={controls}>
      {children}
      {running && <TourOverlay steps={steps} onClose={finish} />}
    </TourContext.Provider>
  )
}
