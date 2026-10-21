import { motion, useScroll, useTransform } from 'motion/react'
import { useContext, useEffect, type ReactNode } from 'react'
import { useResolvedPath } from 'react-router'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { RecordTitleContext, useInPanel } from '@/components/layout/record-panel-state'
import { cn } from '@/lib/utils'

export interface HeaderFigure {
  label: string
  value: ReactNode
  /** Draw attention to it (an amount still due). */
  emphasis?: boolean
}

/**
 * A page's title, description and main actions. In the side panel it stays pinned at the top as the record scrolls,
 * with the record's key figures (figures) beside the title, so the totals are always in view.
 */
export function PageHeader({
  title,
  description,
  actions,
  figures,
}: {
  title: string
  description?: ReactNode
  actions?: ReactNode
  figures?: HeaderFigure[]
}) {
  // The header drifts down and fades a little as the page scrolls under it (not in the side panel).
  const { scrollY } = useScroll()
  const y = useTransform(scrollY, [0, 320], [0, 56])
  const opacity = useTransform(scrollY, [0, 280], [1, 0.4])
  const level = useMotionLevel()
  const inPanel = useInPanel()
  const parallax = level === 'full' && !inPanel

  // In the side panel, tell it which record this is. The route's own path, not the address bar's: a page sliding out
  // must not report its title for the record sliding in.
  const reportTitle = useContext(RecordTitleContext)
  const path = useResolvedPath('.').pathname
  useEffect(() => {
    if (inPanel) reportTitle?.(path, title)
  }, [inPanel, reportTitle, path, title])

  return (
    <motion.div
      className={cn(
        'flex flex-wrap items-start justify-between gap-4',
        inPanel && 'fx-glass sticky top-0 z-20 -mx-4 -mt-4 border-b px-4 pt-4 pb-3 md:-mx-6 md:-mt-6 md:px-6 md:pt-5',
      )}
      style={parallax ? { y, opacity } : undefined}
    >
      <div className="space-y-1">
        {/* A short brand-coloured rule that draws itself above each page's title. */}
        <motion.span
          aria-hidden
          className="mb-2 block h-1 rounded-full"
          style={{ background: 'var(--gradient-brand)' }}
          initial={{ width: 0, opacity: 0 }}
          animate={{ width: 40, opacity: 1 }}
          transition={{ duration: 0.7, delay: 0.15, ease: [0.16, 1, 0.3, 1] }}
        />
        <h2 className={cn('font-semibold tracking-tight', inPanel ? 'text-xl' : 'text-2xl')}>{title}</h2>
        {description && <div className="text-sm text-muted-foreground">{description}</div>}
        {figures && figures.length > 0 && (
          <dl className="flex flex-wrap gap-x-5 gap-y-1 pt-1">
            {figures.map((figure) => (
              <div key={figure.label} className="flex items-baseline gap-1.5">
                <dt className="text-xs text-muted-foreground">{figure.label}</dt>
                <dd className={cn('text-sm font-semibold tabular-nums', figure.emphasis && 'text-brand')}>{figure.value}</dd>
              </div>
            ))}
          </dl>
        )}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </motion.div>
  )
}
