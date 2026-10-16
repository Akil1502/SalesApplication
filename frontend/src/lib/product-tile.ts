/** A product's initials, for its tile until it has a photo. */
export function initials(name: string | undefined): string {
  if (!name) return ''
  const words = name.replace(/[^\p{L}\p{N} ]/gu, ' ').trim().split(/\s+/)
  return ((words[0]?.[0] ?? '') + (words[1]?.[0] ?? '')).toUpperCase()
}

/** A soft tile in a colour of its own for each product, until it has a photo. */
export function placeholderStyle(productId: number): React.CSSProperties {
  const hue = (productId * 47) % 360
  return { background: `linear-gradient(135deg, oklch(0.93 0.04 ${hue}), oklch(0.82 0.08 ${hue}))` }
}
