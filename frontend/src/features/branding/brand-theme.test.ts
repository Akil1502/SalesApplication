import { describe, expect, it } from 'vitest'
import { contrast, forWhiteText, hexToOklch, luminance, toHex, type Oklch } from '@/lib/colour'
import { brandCss, brandTheme, DEFAULT_BRAND_COLOUR } from './brand-theme'

function parse(value: string): Oklch {
  const match = /^oklch\(([\d.]+) ([\d.]+) ([\d.]+)/.exec(value)
  if (!match) throw new Error(`Not an oklch colour: ${value}`)
  return { l: Number(match[1]), c: Number(match[2]), h: Number(match[3]) }
}

const ratio = (a: string, b: string) => contrast(luminance(parse(a)), luminance(parse(b)))

describe('colour maths', () => {
  it('reads hex colours as OKLCH and back', () => {
    const indigo = hexToOklch(DEFAULT_BRAND_COLOUR)!
    expect(indigo.l).toBeCloseTo(0.5, 2)
    expect(indigo.c).toBeCloseTo(0.19, 2)
    expect(indigo.h).toBeCloseTo(268, 0)
    expect(toHex(indigo)).toBe(DEFAULT_BRAND_COLOUR)
    expect(hexToOklch('maroon')).toBeNull()
  })

  it('darkens a light colour just enough for white text, keeping its hue', () => {
    const saffron = hexToOklch('#f4a300')!
    const fitted = forWhiteText(saffron)
    expect(contrast(1, luminance(fitted))).toBeGreaterThanOrEqual(4.6)
    expect(contrast(1, luminance({ ...fitted, l: fitted.l + 0.01 }))).toBeLessThan(4.6)
    expect(Math.abs(fitted.h - saffron.h)).toBeLessThan(1)
  })
})

describe('brand theme', () => {
  it('rebuilds the standard indigo from its own colour', () => {
    const theme = brandTheme(DEFAULT_BRAND_COLOUR)!
    const primary = parse(theme.light['--primary']!)
    expect(primary.l).toBeCloseTo(0.5, 2)
    expect(primary.h).toBeCloseTo(268, 0)
    expect(theme.adjusted).toBe(false)
    expect(parse(theme.dark['--primary']!).l).toBeCloseTo(0.7, 2)
  })

  it('keeps a dark colour exactly as chosen', () => {
    const theme = brandTheme('#7a1f2b')!
    expect(theme.adjusted).toBe(false)
    expect(theme.buttonHex).toBe('#7a1f2b')
  })

  it.each(['#ffffff', '#000000', '#ffff00', '#00ffff', '#ff0000', '#808080', '#f4a300', '#1a237e', '#7a1f2b', '#0f766e'])(
    'keeps text readable on every shade for %s',
    (hex) => {
      const theme = brandTheme(hex)!
      expect(ratio(theme.light['--primary']!, theme.light['--primary-foreground']!)).toBeGreaterThanOrEqual(4.5)
      expect(ratio(theme.dark['--primary']!, theme.dark['--primary-foreground']!)).toBeGreaterThanOrEqual(4.5)
      expect(ratio(theme.light['--background']!, theme.light['--muted-foreground']!)).toBeGreaterThanOrEqual(4.5)
      expect(ratio(theme.dark['--background']!, theme.dark['--muted-foreground']!)).toBeGreaterThanOrEqual(4.5)
      for (const stop of theme.light['--gradient-brand']!.match(/oklch\([^)]*\)/g)!) {
        expect(ratio(stop, 'oklch(1 0 0)')).toBeGreaterThanOrEqual(4.5)
      }
    },
  )

  it('says when the buttons needed a darker shade', () => {
    const theme = brandTheme('#ffff00')!
    expect(theme.adjusted).toBe(true)
    expect(theme.buttonHex).not.toBe('#ffff00')
  })

  it('writes a style sheet that beats the built-in theme and covers dark mode', () => {
    const sheet = brandCss(brandTheme('#0f766e')!)
    expect(sheet.startsWith('html:root,html .theme-light{--background:')).toBe(true)
    expect(sheet).toContain('html.dark{')
    expect(sheet).not.toMatch(/[<>]|url\(|@import/)
  })
})
