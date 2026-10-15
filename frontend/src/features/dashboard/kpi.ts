/** Change as a fraction (0.12 = up 12%), or null when there's nothing to compare against. */
export function change(current: number, previous: number): number | null {
  return previous > 0 ? (current - previous) / previous : null
}
