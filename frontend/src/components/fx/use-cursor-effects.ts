import { useEffect } from 'react'
import { useMediaQuery, useMotionLevel } from './preferences-context'

/**
 * Two pointer effects for the whole app, at full motion with a mouse: a soft light that follows the pointer across
 * cards, and primary buttons that lean a few pixels towards it. One listener, at most one update per frame.
 */
export function useCursorEffects() {
  const level = useMotionLevel()
  const finePointer = useMediaQuery('(hover: hover) and (pointer: fine)')

  useEffect(() => {
    if (level !== 'full' || !finePointer) return
    let frame = 0
    let latest: PointerEvent | null = null
    let leaning: HTMLElement | null = null

    const release = () => {
      leaning?.style.removeProperty('--magnet-x')
      leaning?.style.removeProperty('--magnet-y')
      leaning = null
    }

    const apply = () => {
      frame = 0
      const event = latest
      const target = event?.target instanceof Element ? event.target : null
      if (!event || !target) return

      const card = target.closest<HTMLElement>('[data-slot="card"]')
      if (card) {
        const box = card.getBoundingClientRect()
        card.style.setProperty('--spot-x', `${event.clientX - box.left}px`)
        card.style.setProperty('--spot-y', `${event.clientY - box.top}px`)
      }

      const button = target.closest<HTMLElement>('[data-slot="button"][data-variant="default"]')
      if (button !== leaning) release()
      if (button) {
        const box = button.getBoundingClientRect()
        const dx = (event.clientX - (box.left + box.width / 2)) / (box.width / 2)
        const dy = (event.clientY - (box.top + box.height / 2)) / (box.height / 2)
        button.style.setProperty('--magnet-x', `${(dx * 4).toFixed(2)}px`)
        button.style.setProperty('--magnet-y', `${(dy * 3).toFixed(2)}px`)
        leaning = button
      }
    }

    const onMove = (event: PointerEvent) => {
      latest = event
      if (!frame) frame = requestAnimationFrame(apply)
    }

    document.addEventListener('pointermove', onMove, { passive: true })
    return () => {
      document.removeEventListener('pointermove', onMove)
      cancelAnimationFrame(frame)
      release()
    }
  }, [level, finePointer])
}
