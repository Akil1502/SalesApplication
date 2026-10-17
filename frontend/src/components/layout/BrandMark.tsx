import { ChartNoAxesCombined } from 'lucide-react'
import { useBranding } from '@/features/branding/api'
import { cn } from '@/lib/utils'

/**
 * The app's mark: the company's logo on a white tile once it has one (logos are drawn for light backgrounds), or a
 * gradient tile with a soft glow.
 */
export function BrandMark({ className }: { className?: string }) {
  const logoUrl = useBranding().data?.logoUrl
  if (logoUrl) {
    return (
      <span className={cn('flex aspect-square size-9 shrink-0 items-center justify-center overflow-hidden rounded-xl bg-white p-1 shadow-[0_4px_14px_-2px_var(--brand-glow)]', className)}>
        <img src={logoUrl} alt="" className="size-full object-contain" />
      </span>
    )
  }
  return (
    <span
      className={cn('flex aspect-square size-9 shrink-0 items-center justify-center rounded-xl text-white shadow-[0_4px_14px_-2px_var(--brand-glow)]', className)}
      style={{ background: 'var(--gradient-brand)' }}
    >
      <ChartNoAxesCombined className="size-4.5" />
    </span>
  )
}
