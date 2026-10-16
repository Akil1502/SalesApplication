import { animate } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import { useMotionLevel } from './preferences-context'

/**
 * A number that counts up to its value (and from one value to the next). Screen readers get the final value
 * straight away; the counting is decoration.
 */
export function CountUp({ value, format, className }: { value: number; format: (value: number) => string; className?: string }) {
  const level = useMotionLevel()
  const ref = useRef<HTMLSpanElement>(null)
  const shown = useRef(level === 'off' ? value : 0)
  // Rendered once: after that the animation owns the text, so a re-render never fights it.
  const [initial] = useState(() => format(level === 'off' ? value : 0))

  useEffect(() => {
    const element = ref.current
    if (!element) return
    if (level === 'off') {
      shown.current = value
      element.textContent = format(value)
      return
    }
    const controls = animate(shown.current, value, {
      duration: level === 'full' ? 1.4 : 0.5,
      ease: [0.16, 1, 0.3, 1],
      onUpdate: (current) => {
        shown.current = current
        element.textContent = format(current)
      },
    })
    return () => controls.stop()
  }, [value, level, format])

  return (
    <span className={className}>
      <span ref={ref} aria-hidden className="tabular-nums">
        {initial}
      </span>
      <span className="sr-only">{format(value)}</span>
    </span>
  )
}
