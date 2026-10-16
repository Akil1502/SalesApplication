import { motion, useScroll, useTransform } from 'motion/react'
import type { ReactNode } from 'react'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { useInPanel } from '@/components/layout/record-panel-state'

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  // The header drifts down and fades a little as the page scrolls under it (not in the side panel).
  const { scrollY } = useScroll()
  const y = useTransform(scrollY, [0, 320], [0, 56])
  const opacity = useTransform(scrollY, [0, 280], [1, 0.4])
  const level = useMotionLevel()
  const inPanel = useInPanel()
  const parallax = level === 'full' && !inPanel

  return (
    <motion.div className="flex flex-wrap items-start justify-between gap-4" style={parallax ? { y, opacity } : undefined}>
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
        <h2 className="text-2xl font-semibold tracking-tight">{title}</h2>
        {description && <div className="text-sm text-muted-foreground">{description}</div>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </motion.div>
  )
}
