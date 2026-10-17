import { motion } from 'motion/react'
import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface SegmentedControlProps<T extends string> {
  value: T
  onValueChange: (value: T) => void
  options: readonly { value: T; label: ReactNode }[]
  /** What the choice is about, for screen readers ("Show", "Kind of credit note"). */
  label: string
  /** Stretch the options across the full width. */
  fullWidth?: boolean
  className?: string
}

/**
 * One choice from a few, side by side (a list's filter, a form's mode), with a highlight that slides to the chosen
 * one. A radio group to assistive technology: Tab reaches the chosen option and the arrow keys change it. Use Tabs
 * only when each choice shows its own panel.
 */
export function SegmentedControl<T extends string>({ value, onValueChange, options, label, fullWidth, className }: SegmentedControlProps<T>) {
  const id = useId()
  const buttons = useRef<(HTMLButtonElement | null)[]>([])

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0
    if (!step) return
    event.preventDefault()
    const next = (index + step + options.length) % options.length
    onValueChange(options[next]!.value)
    buttons.current[next]?.focus()
  }

  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={cn('inline-flex flex-wrap items-center gap-0.5 rounded-lg bg-muted p-[3px]', fullWidth && 'flex w-full', className)}
    >
      {options.map((option, index) => {
        const selected = option.value === value
        return (
          <button
            key={option.value}
            ref={(element) => void (buttons.current[index] = element)}
            type="button"
            role="radio"
            aria-checked={selected}
            tabIndex={selected ? 0 : -1}
            onClick={() => onValueChange(option.value)}
            onKeyDown={(event) => onKeyDown(event, index)}
            className={cn(
              'relative isolate h-7 rounded-md px-3 text-sm font-medium whitespace-nowrap transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring',
              selected ? 'text-foreground' : 'text-muted-foreground hover:text-foreground',
              fullWidth && 'flex-1',
            )}
          >
            {selected && (
              <motion.span
                layoutId={`segment-${id}`}
                aria-hidden
                className="absolute inset-0 -z-10 rounded-md bg-background shadow-sm ring-1 ring-foreground/5 dark:bg-input/50"
                transition={{ type: 'spring', stiffness: 500, damping: 38 }}
              />
            )}
            {option.label}
          </button>
        )
      })}
    </div>
  )
}
