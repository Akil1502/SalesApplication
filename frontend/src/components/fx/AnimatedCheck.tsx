/** A tick that draws itself inside a circle that pops in: the "saved" moment on success messages. */
export function AnimatedCheck({ className = 'size-4' }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden fill="none">
      <circle
        cx="12"
        cy="12"
        r="10"
        fill="currentColor"
        opacity="0.15"
        style={{ transformOrigin: 'center', animation: 'fx-pop 0.45s var(--ease-out) backwards' }}
      />
      <circle
        cx="12"
        cy="12"
        r="10"
        stroke="currentColor"
        strokeWidth="2"
        strokeDasharray="63"
        strokeDashoffset="63"
        style={{ animation: 'fx-draw 0.5s ease-out forwards' }}
      />
      <path
        d="M7.5 12.5l3 3 6-6.5"
        stroke="currentColor"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeDasharray="16"
        strokeDashoffset="16"
        style={{ animation: 'fx-draw 0.35s 0.35s ease-out forwards' }}
      />
    </svg>
  )
}
