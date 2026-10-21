import { useId } from 'react'
import { cn } from '@/lib/utils'

/**
 * The empty-state illustration: a yarn cone floating over an open carton, its thread drawing itself down into the
 * box. Drawn in SVG with the theme's colours, so it suits light and dark and needs no picture files.
 */
export function EmptyArt({ className }: { className?: string }) {
  const id = useId().replace(/:/g, '')
  const yarn = `yarn-${id}`
  const box = `box-${id}`
  const lid = `lid-${id}`

  return (
    <svg viewBox="0 0 160 120" className={cn('h-24 w-32', className)} aria-hidden>
      <defs>
        <linearGradient id={yarn} x1="0" x2="1" y1="0" y2="0">
          <stop offset="0" stopColor="var(--brand)" />
          <stop offset="1" stopColor="var(--brand-2)" />
        </linearGradient>
        <linearGradient id={box} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stopColor="color-mix(in oklch, var(--brand) 22%, var(--card))" />
          <stop offset="1" stopColor="color-mix(in oklch, var(--brand) 38%, var(--card))" />
        </linearGradient>
        <linearGradient id={lid} x1="0" x2="1" y1="0" y2="1">
          <stop offset="0" stopColor="color-mix(in oklch, var(--brand) 12%, var(--card))" />
          <stop offset="1" stopColor="color-mix(in oklch, var(--brand) 28%, var(--card))" />
        </linearGradient>
      </defs>

      {/* Shadow under the carton. */}
      <ellipse cx="82" cy="108" rx="44" ry="6" fill="var(--foreground)" opacity="0.08" />

      {/* The carton: back flaps, body, front flaps. */}
      <path d="M44 62 L60 50 L104 50 L120 62 Z" fill={`url(#${lid})`} />
      <path d="M44 62 L120 62 L116 104 L48 104 Z" fill={`url(#${box})`} />
      <path d="M44 62 L32 74 L56 74 L60 62 Z" fill={`url(#${lid})`} opacity="0.9" />
      <path d="M120 62 L132 74 L108 74 L104 62 Z" fill={`url(#${lid})`} opacity="0.9" />
      <path d="M62 80 L102 80" stroke="var(--card)" strokeWidth="3" strokeLinecap="round" opacity="0.6" />

      {/* The thread, drawn from the cone down into the box. */}
      <path
        d="M96 34 C 108 44, 70 46, 80 62"
        fill="none"
        stroke="var(--brand-2)"
        strokeWidth="1.6"
        strokeLinecap="round"
        strokeDasharray="70"
        strokeDashoffset="70"
        style={{ animation: 'fx-draw 1.6s 0.4s ease-out forwards' }}
      />

      {/* The yarn cone, floating. */}
      <g className="fx-ambient" style={{ animation: 'fx-float 5s ease-in-out infinite', transformOrigin: '96px 22px' }}>
        <path d="M84 36 L90 8 L102 8 L108 36 Z" fill={`url(#${yarn})`} />
        <path d="M86 28 L106 28 M87 22 L105 22 M88 16 L104 16" stroke="var(--card)" strokeWidth="1" opacity="0.35" />
        <ellipse cx="96" cy="36" rx="12" ry="3" fill="color-mix(in oklch, var(--brand) 70%, black)" opacity="0.5" />
        <rect x="93.5" y="2" width="5" height="8" rx="1.5" fill="color-mix(in oklch, var(--brand-2) 40%, var(--card))" />
      </g>

      {/* A few sparkles. */}
      <g className="fx-ambient" style={{ animation: 'fx-float 4s ease-in-out -1s infinite' }}>
        <circle cx="40" cy="30" r="2" fill="var(--brand-2)" opacity="0.6" />
        <circle cx="128" cy="40" r="1.6" fill="var(--brand)" opacity="0.5" />
        <circle cx="58" cy="18" r="1.2" fill="var(--brand)" opacity="0.4" />
      </g>
    </svg>
  )
}
