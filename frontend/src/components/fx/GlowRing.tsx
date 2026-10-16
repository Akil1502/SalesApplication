import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/** A thin border of brand colours that slowly circles its content, for the AI entry points. */
export function GlowRing({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <span className={cn('relative inline-flex overflow-hidden rounded-lg p-[1.5px]', className)}>
      <span
        aria-hidden
        className="fx-ambient pointer-events-none absolute top-1/2 left-1/2 aspect-square w-[250%] -translate-x-1/2 -translate-y-1/2"
        style={{
          background: 'conic-gradient(var(--brand), var(--brand-2), oklch(0.7 0.16 310), var(--brand))',
          animation: 'fx-spin 4s linear infinite',
        }}
      />
      <span className="relative inline-flex rounded-[calc(var(--radius-lg)-1.5px)] bg-background">{children}</span>
    </span>
  )
}
