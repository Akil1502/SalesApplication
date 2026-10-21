import { useIsFetching, useIsMutating } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useNavigation } from 'react-router'

/**
 * A thin glowing bar along the top while a page or data is loading. It waits a moment before showing, so quick
 * requests don't flicker it.
 */
export function TopProgress() {
  const navigating = useNavigation().state !== 'idle'
  const busy = useIsFetching() + useIsMutating() > 0 || navigating
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    const timer = setTimeout(() => setVisible(busy), busy ? 180 : 250)
    return () => clearTimeout(timer)
  }, [busy])

  return (
    <div
      aria-hidden
      className={`pointer-events-none absolute inset-x-0 top-0 z-20 h-0.5 overflow-hidden transition-opacity duration-300 ${visible ? 'opacity-100' : 'opacity-0'}`}
    >
      <div
        className="h-full w-1/2 rounded-full"
        style={{
          background: 'var(--gradient-brand)',
          boxShadow: '0 0 12px var(--brand-glow)',
          animation: visible ? 'fx-progress 1.1s ease-in-out infinite' : undefined,
        }}
      />
    </div>
  )
}
