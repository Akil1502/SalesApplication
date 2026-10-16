import { ChartNoAxesCombined } from 'lucide-react'
import { cn } from '@/lib/utils'

/** The app's mark: a gradient tile with a soft glow. */
export function BrandMark({ className }: { className?: string }) {
  return (
    <span
      className={cn('flex aspect-square size-9 shrink-0 items-center justify-center rounded-xl text-white shadow-[0_4px_14px_-2px_var(--brand-glow)]', className)}
      style={{ background: 'var(--gradient-brand)' }}
    >
      <ChartNoAxesCombined className="size-4.5" />
    </span>
  )
}
