/** Brand and celebration colours for the paper pieces. */
const COLOURS = ['#4a52d4', '#2a9fb0', '#7f86f5', '#e3a03a', '#d4798a', '#5fd3a0']

/**
 * A small burst of confetti from a point on screen, for real milestones (an invoice settled, a record month). Plain
 * DOM pieces animated by the browser, removed as they land. Only at full motion; does nothing otherwise.
 */
export function confettiBurst(x: number, y: number, { count = 70, spread = 1 } = {}) {
  if (document.documentElement.dataset.motion !== 'full' || typeof Element.prototype.animate !== 'function') return

  const layer = document.createElement('div')
  layer.setAttribute('aria-hidden', 'true')
  layer.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:100;overflow:hidden'
  document.body.appendChild(layer)

  let remaining = count
  for (let i = 0; i < count; i++) {
    const piece = document.createElement('span')
    const size = 6 + Math.random() * 6
    const round = Math.random() < 0.3
    piece.style.cssText =
      `position:absolute;left:${x}px;top:${y}px;width:${size}px;height:${round ? size : size * 0.45}px;` +
      `background:${COLOURS[i % COLOURS.length]};border-radius:${round ? '50%' : '2px'};will-change:transform,opacity`
    layer.appendChild(piece)

    const angle = -Math.PI / 2 + (Math.random() - 0.5) * Math.PI * 1.2 * spread
    const speed = 160 + Math.random() * 260
    const dx = Math.cos(angle) * speed
    const dy = Math.sin(angle) * speed
    const fall = 220 + Math.random() * 260
    const turns = (Math.random() - 0.5) * 1080

    piece
      .animate(
        [
          { transform: 'translate(-50%, -50%) rotate(0deg) scale(1)', opacity: 1 },
          { transform: `translate(calc(-50% + ${dx}px), calc(-50% + ${dy}px)) rotate(${turns / 2}deg) scale(1)`, opacity: 1, offset: 0.35 },
          { transform: `translate(calc(-50% + ${dx * 1.3}px), calc(-50% + ${dy + fall}px)) rotate(${turns}deg) scale(0.8)`, opacity: 0 },
        ],
        { duration: 1400 + Math.random() * 900, easing: 'cubic-bezier(0.2, 0.7, 0.3, 1)', fill: 'forwards' },
      )
      .finished.finally(() => {
        piece.remove()
        remaining -= 1
        if (remaining === 0) layer.remove()
      })
  }
}

/** Confetti from the middle of an element. */
export function confettiFrom(element: Element | null, options?: { count?: number; spread?: number }) {
  if (!element) return
  const box = element.getBoundingClientRect()
  confettiBurst(box.left + box.width / 2, box.top + box.height / 2, options)
}
