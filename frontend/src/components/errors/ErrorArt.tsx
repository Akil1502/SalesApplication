import { cn } from '@/lib/utils'

/** A yarn cone whose thread has snapped, the loose end swinging: the picture for "something broke". */
export function ErrorArt({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 160 120" className={cn('h-28 w-36', className)} aria-hidden>
      <defs>
        <linearGradient id="error-cone" x1="0" x2="1" y1="0" y2="0">
          <stop offset="0" stopColor="var(--brand)" />
          <stop offset="1" stopColor="var(--brand-2)" />
        </linearGradient>
      </defs>
      <ellipse cx="62" cy="108" rx="34" ry="5" fill="var(--foreground)" opacity="0.08" />
      {/* The cone. */}
      <path d="M44 100 L54 44 L70 44 L80 100 Z" fill="url(#error-cone)" />
      <path d="M47 88 L77 88 M49 76 L75 76 M51 64 L73 64 M53 52 L71 52" stroke="var(--card)" strokeWidth="1" opacity="0.35" />
      <rect x="58" y="34" width="8" height="12" rx="2" fill="color-mix(in oklch, var(--brand-2) 40%, var(--card))" />
      {/* The thread up to where it snapped. */}
      <path d="M62 36 C 70 20, 92 18, 100 26" fill="none" stroke="var(--brand-2)" strokeWidth="1.6" strokeLinecap="round" />
      {/* The loose end, swinging. */}
      <g className="fx-ambient" style={{ transformOrigin: '112px 30px', animation: 'fx-swing 2.4s ease-in-out infinite' }}>
        <path d="M112 30 C 118 44, 108 54, 116 68" fill="none" stroke="var(--brand-2)" strokeWidth="1.6" strokeLinecap="round" />
      </g>
      {/* The snap. */}
      <g stroke="var(--destructive)" strokeWidth="1.6" strokeLinecap="round" className="fx-ambient" style={{ animation: 'fx-blink 1.6s ease-in-out infinite' }}>
        <path d="M104 22 L101 18" />
        <path d="M107 27 L111 24" />
        <path d="M104 31 L102 35" />
      </g>
    </svg>
  )
}
