import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { useLocation } from 'react-router'
import { panelListOf } from '@/components/layout/record-panel-state'
import { useMotionLevel } from './preferences-context'

/**
 * Each page swings up into place from slightly below and behind the screen. Keyed by the page, so moving to
 * another page plays it, but a filter, another list page or a record opening in the side panel doesn't. The
 * transform ends at none, so fixed and sticky parts of the page behave normally once it settles.
 */
export function PageTransition({ children }: { children: ReactNode }) {
  const { pathname } = useLocation()
  const level = useMotionLevel()

  if (level === 'off') return <div className="flex flex-1 flex-col gap-4">{children}</div>

  const full = level === 'full'
  return (
    <motion.div
      key={panelListOf(pathname) ?? pathname}
      className="flex flex-1 flex-col gap-4"
      initial={full ? { opacity: 0, y: 28, rotateX: 7, scale: 0.985 } : { opacity: 0 }}
      animate={{ opacity: 1, y: 0, rotateX: 0, scale: 1 }}
      transition={{ duration: full ? 0.6 : 0.25, ease: [0.16, 1, 0.3, 1] }}
      style={{ transformPerspective: 1600, transformOrigin: '50% 0%' }}
    >
      {children}
    </motion.div>
  )
}
