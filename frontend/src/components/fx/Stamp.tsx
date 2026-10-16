import { motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import { cn } from '@/lib/utils'
import { confettiFrom } from './confetti'
import { useMotionLevel } from './preferences-context'

/**
 * A rubber stamp ("PAID") that thuds down onto a document. With celebrate, it also throws a little confetti: for
 * when it lands while you're watching (a payment just settled the invoice), not every time the page opens.
 */
export function Stamp({ label, celebrate = false, className }: { label: string; celebrate?: boolean; className?: string }) {
  const level = useMotionLevel()
  const ref = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!celebrate) return
    const timer = setTimeout(() => confettiFrom(ref.current, { count: 60 }), 380)
    return () => clearTimeout(timer)
  }, [celebrate])

  return (
    <motion.span
      ref={ref}
      role="img"
      aria-label={label}
      className={cn(
        'pointer-events-none inline-flex items-center rounded-md border-[3px] border-current px-3 py-0.5 font-mono text-lg font-black tracking-[0.2em] uppercase select-none',
        'text-emerald-600/85 dark:text-emerald-400/85',
        className,
      )}
      style={{ maskImage: 'radial-gradient(circle at 30% 40%, black 60%, rgb(0 0 0 / 75%) 100%)' }}
      initial={level === 'off' ? false : { scale: 2.4, rotate: -24, opacity: 0 }}
      animate={{ scale: 1, rotate: -9, opacity: 1 }}
      transition={{ type: 'spring', stiffness: 520, damping: 18, mass: 0.9, delay: 0.25 }}
    >
      {label}
    </motion.span>
  )
}

/**
 * "PAID" on a settled document. Confetti only when it becomes paid while open (the payment was just recorded);
 * key it by the record so moving to another record starts afresh.
 */
export function PaidStamp({ paid, className }: { paid: boolean; className?: string }) {
  const [paidWhenOpened] = useState(paid)
  if (!paid) return null
  return <Stamp label="Paid" celebrate={!paidWhenOpened} className={className} />
}
