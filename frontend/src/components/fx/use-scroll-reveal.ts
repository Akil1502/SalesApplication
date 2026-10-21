import { useEffect } from 'react'
import { useLocation } from 'react-router'
import { useMotionLevel } from './preferences-context'

/**
 * At full motion, cards further down a page wait below the fold and rise into place as they scroll into view (cards
 * on screen when the page opens arrive with the page instead). Watches the page for cards added later, such as
 * when data arrives.
 */
export function useScrollReveal() {
  const level = useMotionLevel()
  const { pathname } = useLocation()

  useEffect(() => {
    const main = document.querySelector('main')
    if (level !== 'full' || !main || typeof IntersectionObserver === 'undefined') return

    const reveal = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue
          const card = entry.target as HTMLElement
          reveal.unobserve(card)
          card.dataset.reveal = 'shown'
          card.addEventListener('transitionend', () => delete card.dataset.reveal, { once: true })
        }
      },
      { rootMargin: '0px 0px -6% 0px', threshold: 0.05 },
    )

    const seen = new WeakSet<Element>()
    let frame = 0
    const scan = () => {
      frame = 0
      for (const card of main.querySelectorAll<HTMLElement>('[data-slot="card"]')) {
        if (seen.has(card)) continue
        seen.add(card)
        if (card.getBoundingClientRect().top > window.innerHeight) {
          card.dataset.reveal = 'pending'
          reveal.observe(card)
        }
      }
    }
    scan()
    const changes = new MutationObserver(() => {
      if (!frame) frame = requestAnimationFrame(scan)
    })
    changes.observe(main, { childList: true, subtree: true })

    return () => {
      cancelAnimationFrame(frame)
      changes.disconnect()
      reveal.disconnect()
      for (const card of main.querySelectorAll<HTMLElement>('[data-reveal]')) delete card.dataset.reveal
    }
  }, [level, pathname])
}
