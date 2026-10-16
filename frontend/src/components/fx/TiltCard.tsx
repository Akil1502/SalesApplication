import { motion, useMotionTemplate, useMotionValue, useSpring, useTransform } from 'motion/react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { useMediaQuery, useMotionLevel } from './preferences-context'

interface TiltCardProps {
  children: ReactNode
  className?: string
  /** Largest tilt, in degrees. */
  max?: number
  /** A soft light that follows the pointer across the surface. */
  glare?: boolean
}

/**
 * Tilts its content towards the pointer in 3D, with a highlight that follows it. Only at full motion and with a
 * mouse or pen: on touch screens and at lower motion levels the content sits still.
 */
export function TiltCard({ children, className, max = 7, glare = true }: TiltCardProps) {
  const level = useMotionLevel()
  const finePointer = useMediaQuery('(hover: hover) and (pointer: fine)')
  const x = useMotionValue(0.5)
  const y = useMotionValue(0.5)
  const spring = { stiffness: 260, damping: 22, mass: 0.6 }
  const rotateX = useSpring(useTransform(y, [0, 1], [max, -max]), spring)
  const rotateY = useSpring(useTransform(x, [0, 1], [-max, max]), spring)
  const glareX = useTransform(x, (v) => `${v * 100}%`)
  const glareY = useTransform(y, (v) => `${v * 100}%`)
  const light = useMotionTemplate`radial-gradient(circle at ${glareX} ${glareY}, oklch(1 0 0 / 28%), transparent 55%)`

  if (level !== 'full' || !finePointer) return <div className={className}>{children}</div>

  return (
    <motion.div
      className={cn('group/tilt relative', className)}
      style={{ rotateX, rotateY, transformPerspective: 900, transformStyle: 'preserve-3d' }}
      onPointerMove={(event) => {
        const box = event.currentTarget.getBoundingClientRect()
        x.set((event.clientX - box.left) / box.width)
        y.set((event.clientY - box.top) / box.height)
      }}
      onPointerLeave={() => {
        x.set(0.5)
        y.set(0.5)
      }}
    >
      {children}
      {glare && (
        <motion.span
          aria-hidden
          className="pointer-events-none absolute inset-0 rounded-xl opacity-0 mix-blend-soft-light transition-opacity duration-300 group-hover/tilt:opacity-100"
          style={{ background: light }}
        />
      )}
    </motion.div>
  )
}
