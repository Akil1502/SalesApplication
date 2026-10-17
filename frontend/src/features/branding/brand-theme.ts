import { clampChroma, css, forDarkText, forWhiteText, hexToOklch, luminance, toHex, turn, type Oklch } from '@/lib/colour'

/** The standard indigo (oklch 0.5 0.19 268), used when the company hasn't chosen a colour. */
export const DEFAULT_BRAND_COLOUR = '#3655cd'

/** The standard palette's chroma for its main colour; other shades are scaled from it. */
const STANDARD_CHROMA = 0.19

export interface BrandTheme {
  light: Record<string, string>
  dark: Record<string, string>
  /** Buttons' colour in the light theme, as #rrggbb. */
  buttonHex: string
  /** Whether the buttons' shade had to differ from the chosen colour so white text on them stays readable. */
  adjusted: boolean
  /** The main and second colour for the 3D scenes, per theme. */
  scene: { light: [string, string]; dark: [string, string] }
}

/**
 * Every colour token the app's theme defines in its brand hue (src/index.css), rebuilt around the chosen colour:
 * the same lightness steps as the standard palette, the chosen hue, and tints scaled to the colour's strength (a
 * grey brand gives plain greys). Shades that carry text are checked for contrast.
 */
export function brandTheme(hex: string): BrandTheme | null {
  const chosen = hexToOklch(hex)
  if (!chosen) return null
  const { h } = chosen
  const k = Math.min(chosen.c / STANDARD_CHROMA, 1.2)
  const tint = (l: number, c: number, hue = h): Oklch => ({ l, c: c * k, h: hue })
  const t = (l: number, c: number, hue = h) => css(tint(l, c, hue))

  // Light theme: near-white text on buttons, so no lighter than that allows.
  const buttonText = tint(0.985, 0.003)
  const button = forWhiteText(clampChroma(chosen), 4.6, luminance(buttonText))
  const gradient = [button, forWhiteText(tint(button.l + 0.02, 0.16, turn(h, -23))), forWhiteText(tint(button.l + 0.03, 0.12, turn(h, -56)))]
  const gradientCss = `linear-gradient(135deg, ${css(gradient[0]!)}, ${css(gradient[1]!)} 55%, ${css(gradient[2]!)})`
  const brand2 = tint(0.66, 0.13, turn(h, -63))

  // Dark theme: dark text on buttons, so a lighter shade (the chosen one if it's already light).
  const darkText = tint(0.16, 0.03)
  const darkButton = forDarkText(chosen.l >= 0.7 ? chosen : { ...chosen, l: 0.7, c: chosen.c * 0.79 }, luminance(darkText))
  const darkBrand2 = tint(0.76, 0.12, turn(h, -68))

  const light: Record<string, string> = {
    '--background': t(0.982, 0.004),
    '--foreground': t(0.21, 0.03),
    '--card-foreground': t(0.21, 0.03),
    '--popover-foreground': t(0.21, 0.03),
    '--primary': css(button),
    '--primary-foreground': css(buttonText),
    '--secondary': t(0.955, 0.012),
    '--secondary-foreground': t(0.27, 0.05),
    '--muted': t(0.958, 0.008),
    '--muted-foreground': t(0.5, 0.025),
    '--accent': t(0.95, 0.018),
    '--accent-foreground': t(0.3, 0.08),
    '--border': t(0.912, 0.01),
    '--input': t(0.89, 0.012),
    '--ring': t(0.62, 0.16),
    '--chart-1': css(button),
    '--sidebar': t(0.225, 0.05),
    '--sidebar-foreground': t(0.93, 0.012),
    '--sidebar-primary': t(0.7, 0.14),
    '--sidebar-primary-foreground': t(0.18, 0.04),
    '--sidebar-accent': t(0.3, 0.06),
    '--sidebar-accent-foreground': t(0.98, 0.005),
    '--sidebar-ring': t(0.7, 0.14),
    '--brand': css(button),
    '--brand-2': css(brand2),
    '--brand-glow': css(tint(0.6, 0.2), 35),
    '--gradient-brand': gradientCss,
    '--shadow-elevated': `0 1px 2px ${css(tint(0.25, 0.05), 6)}, 0 8px 24px -8px ${css(tint(0.25, 0.05), 18)}`,
    '--shadow-lifted': `0 2px 4px ${css(tint(0.25, 0.05), 8)}, 0 18px 40px -12px ${css(tint(0.3, 0.12), 30)}`,
  }

  const dark: Record<string, string> = {
    '--background': t(0.165, 0.026),
    '--foreground': t(0.955, 0.006),
    '--card': t(0.205, 0.03),
    '--card-foreground': t(0.955, 0.006),
    '--popover': t(0.225, 0.032),
    '--popover-foreground': t(0.955, 0.006),
    '--primary': css(darkButton),
    '--primary-foreground': css(darkText),
    '--secondary': t(0.265, 0.032),
    '--secondary-foreground': t(0.955, 0.006),
    '--muted': t(0.255, 0.03),
    '--muted-foreground': t(0.73, 0.02),
    '--accent': t(0.28, 0.045),
    '--accent-foreground': t(0.955, 0.006),
    '--ring': t(0.62, 0.14),
    '--chart-1': css(darkButton),
    '--sidebar': t(0.13, 0.024),
    '--sidebar-foreground': t(0.93, 0.012),
    '--sidebar-primary': css(darkButton),
    '--sidebar-primary-foreground': css(darkText),
    '--sidebar-accent': t(0.24, 0.04),
    '--sidebar-accent-foreground': t(0.98, 0.005),
    '--sidebar-ring': t(0.62, 0.14),
    '--brand': css(darkButton),
    '--brand-2': css(darkBrand2),
    '--brand-glow': css(tint(0.65, 0.18), 40),
    '--gradient-brand': gradientCss,
    '--surface-glass': css(tint(0.2, 0.03), 70),
    '--shadow-lifted': `0 2px 6px oklch(0 0 0 / 35%), 0 22px 48px -14px ${css(tint(0.45, 0.15), 45)}`,
  }

  return {
    light,
    dark,
    buttonHex: toHex(button),
    adjusted: Math.abs(button.l - chosen.l) > 0.005,
    scene: { light: [toHex(button), toHex(brand2)], dark: [toHex(darkButton), toHex(darkBrand2)] },
  }
}

/**
 * The theme as a style sheet. One step more specific than src/index.css (html + class), so it wins whichever loads
 * first; printed documents (.theme-light) keep the light shades even in dark mode.
 */
export function brandCss(theme: BrandTheme): string {
  const block = (tokens: Record<string, string>) => Object.entries(tokens).map(([name, value]) => `${name}:${value}`).join(';')
  return `html:root,html .theme-light{${block(theme.light)}}html.dark{${block(theme.dark)}}`
}
