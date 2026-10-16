/** A beam of light sweeping down over what the AI is reading, while it reads. */
export function ScanOverlay() {
  return (
    <div aria-hidden className="pointer-events-none absolute inset-0 overflow-hidden rounded-lg">
      <div
        className="absolute inset-x-0 h-20 -translate-y-1/2"
        style={{
          background: 'linear-gradient(180deg, transparent, color-mix(in oklch, var(--brand-2) 22%, transparent), transparent)',
          animation: 'fx-scan 2s ease-in-out infinite',
        }}
      />
      <div
        className="absolute inset-x-0 h-px"
        style={{ background: 'var(--brand-2)', boxShadow: '0 0 14px 2px var(--brand-2)', animation: 'fx-scan 2s ease-in-out infinite' }}
      />
    </div>
  )
}
