import { cn } from '@/lib/utils'

/** The AI ball without 3D: a lit sphere with a soft glow that breathes while the AI works. */
export function StillOrb({ active = false, className }: { active?: boolean; className?: string }) {
  return (
    <div aria-hidden className={cn('relative flex items-center justify-center', className)}>
      <span
        className="fx-ambient absolute inset-[14%] rounded-full opacity-60 blur-2xl"
        style={{ background: 'var(--gradient-brand)', animation: `fx-breathe ${active ? '1.2s' : '4s'} ease-in-out infinite` }}
      />
      <span
        className="relative aspect-square w-[62%] rounded-full shadow-[0_18px_40px_-12px_var(--brand-glow)]"
        style={{ background: 'radial-gradient(circle at 32% 28%, #d4d6ff 0%, #7a80f2 26%, #4248c4 58%, #1d2160 100%)' }}
      />
    </div>
  )
}
