/**
 * Slow-drifting coloured light behind the work area, with a faint dot grid. Purely decoration: it sits behind
 * everything, ignores the pointer, and only drifts at full motion.
 */
export function AuroraBackground() {
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 -z-10 overflow-hidden print:hidden">
      <div
        className="fx-ambient absolute -top-[20%] left-[10%] size-[60vmax] rounded-full opacity-[0.10] blur-3xl dark:opacity-[0.16]"
        style={{ background: 'radial-gradient(circle, var(--brand) 0%, transparent 65%)', animation: 'fx-drift 26s ease-in-out infinite' }}
      />
      <div
        className="fx-ambient absolute top-[30%] -right-[15%] size-[55vmax] rounded-full opacity-[0.09] blur-3xl dark:opacity-[0.14]"
        style={{ background: 'radial-gradient(circle, var(--brand-2) 0%, transparent 65%)', animation: 'fx-drift 32s ease-in-out -8s infinite reverse' }}
      />
      <div
        className="fx-ambient absolute -bottom-[25%] left-[35%] size-[50vmax] rounded-full opacity-[0.07] blur-3xl dark:opacity-[0.12]"
        style={{ background: 'radial-gradient(circle, oklch(0.62 0.17 300) 0%, transparent 65%)', animation: 'fx-drift 38s ease-in-out -16s infinite' }}
      />
      <div
        className="absolute inset-0 opacity-[0.35] dark:opacity-[0.25]"
        style={{
          backgroundImage: 'radial-gradient(color-mix(in oklch, var(--foreground) 14%, transparent) 1px, transparent 1px)',
          backgroundSize: '22px 22px',
          maskImage: 'radial-gradient(ellipse at 50% 0%, black 0%, transparent 70%)',
        }}
      />
    </div>
  )
}
