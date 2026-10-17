/**
 * Colour maths for the company's brand colour. Any colour can be chosen, so the app works in OKLCH (where lightness
 * matches what the eye sees) and checks WCAG contrast itself: shades that carry text are moved lighter or darker
 * just far enough for the text to stay readable, keeping the colour's hue and as much of its strength as fits.
 */

export interface Oklch {
  l: number
  c: number
  h: number
}

type Rgb = [number, number, number]

const toLinear = (c: number) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4)
const toGamma = (c: number) => (c <= 0.0031308 ? 12.92 * c : 1.055 * c ** (1 / 2.4) - 0.055)

/** #rrggbb as linear-light sRGB, or null when it isn't a colour. */
export function parseHex(hex: string): Rgb | null {
  const match = /^#?([0-9a-f]{6})$/i.exec(hex.trim())
  if (!match) return null
  const value = parseInt(match[1]!, 16)
  return [(value >> 16) & 255, (value >> 8) & 255, value & 255].map((c) => toLinear(c / 255)) as Rgb
}

export function hexToOklch(hex: string): Oklch | null {
  const rgb = parseHex(hex)
  if (!rgb) return null
  const [r, g, b] = rgb
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
  const L = 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s
  const A = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s
  const B = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s
  const c = Math.hypot(A, B)
  return { l: L, c, h: c < 1e-4 ? 0 : (((Math.atan2(B, A) * 180) / Math.PI) % 360 + 360) % 360 }
}

function oklchToLinear({ l: L, c, h }: Oklch): Rgb {
  const a = c * Math.cos((h * Math.PI) / 180)
  const b = c * Math.sin((h * Math.PI) / 180)
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ]
}

const inGamut = (colour: Oklch) => oklchToLinear(colour).every((v) => v >= -1e-4 && v <= 1 + 1e-4)

/** The colour with its chroma reduced just enough to be shown on an ordinary (sRGB) screen. */
export function clampChroma(colour: Oklch): Oklch {
  if (inGamut(colour)) return colour
  let low = 0
  let high = colour.c
  for (let i = 0; i < 24; i++) {
    const mid = (low + high) / 2
    if (inGamut({ ...colour, c: mid })) low = mid
    else high = mid
  }
  return { ...colour, c: low }
}

/** WCAG relative luminance. */
export function luminance(colour: Oklch): number {
  const [r, g, b] = oklchToLinear(clampChroma(colour)).map((v) => Math.min(1, Math.max(0, v))) as Rgb
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

export const contrast = (a: number, b: number) => (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)

/** The lightest version of the colour (no lighter than it is) that light text (white by default) can sit on. */
export function forWhiteText(colour: Oklch, minimum = 4.6, textLuminance = 1): Oklch {
  const fits = (l: number) => contrast(textLuminance, luminance({ ...colour, l })) >= minimum
  if (fits(colour.l)) return clampChroma(colour)
  let low = 0
  let high = colour.l
  for (let i = 0; i < 24; i++) {
    const mid = (low + high) / 2
    if (fits(mid)) low = mid
    else high = mid
  }
  return clampChroma({ ...colour, l: low })
}

/** The darkest version of the colour (no darker than it is) that text of the given luminance can sit on. */
export function forDarkText(colour: Oklch, textLuminance: number, minimum = 4.6): Oklch {
  const fits = (l: number) => contrast(textLuminance, luminance({ ...colour, l })) >= minimum
  if (fits(colour.l)) return clampChroma(colour)
  let low = colour.l
  let high = 1
  for (let i = 0; i < 24; i++) {
    const mid = (low + high) / 2
    if (fits(mid)) high = mid
    else low = mid
  }
  return clampChroma({ ...colour, l: high })
}

const round = (value: number, places: number) => Number(value.toFixed(places))

/** As a CSS colour, e.g. oklch(0.5 0.19 268) or oklch(0.6 0.2 268 / 35%). */
export function css(colour: Oklch, alpha?: number): string {
  const { l, c, h } = clampChroma(colour)
  const body = `${round(l, 3)} ${round(c, 3)} ${round(h, 1)}`
  return alpha === undefined ? `oklch(${body})` : `oklch(${body} / ${alpha}%)`
}

export function toHex(colour: Oklch): string {
  return `#${oklchToLinear(clampChroma(colour))
    .map((v) => Math.round(Math.min(1, Math.max(0, toGamma(Math.min(1, Math.max(0, v))))) * 255).toString(16).padStart(2, '0'))
    .join('')}`
}

/** Turns a hue by the given degrees. */
export const turn = (h: number, degrees: number) => (((h + degrees) % 360) + 360) % 360
