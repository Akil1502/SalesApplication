import { CanvasTexture, ClampToEdgeWrapping, RepeatWrapping, SRGBColorSpace } from 'three'

/** Repeatable "random" numbers, so every texture looks the same on every load. */
export function random(seed: number) {
  let state = seed
  return () => {
    state = (state + 0x6d2b79f5) | 0
    let t = Math.imul(state ^ (state >>> 15), 1 | state)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function canvas(size: number): [HTMLCanvasElement, CanvasRenderingContext2D] {
  const element = document.createElement('canvas')
  element.width = element.height = size
  const context = element.getContext('2d')
  if (!context) throw new Error('2D canvas unavailable')
  return [element, context]
}

function texture(element: HTMLCanvasElement, repeatX: number, repeatY: number): CanvasTexture {
  const result = new CanvasTexture(element)
  result.wrapS = result.wrapT = RepeatWrapping
  result.repeat.set(repeatX, repeatY)
  result.colorSpace = SRGBColorSpace
  result.anisotropy = 4
  return result
}

const cache = new Map<string, CanvasTexture>()

function cached(key: string, make: () => CanvasTexture): CanvasTexture {
  let value = cache.get(key)
  if (!value) {
    value = make()
    cache.set(key, value)
  }
  return value
}

/**
 * Fine criss-cross threads, the look of yarn wound on a cone. Light grey, so a material's colour tints it to any
 * shade; lines that run off one edge come back on the other, so it repeats without seams.
 */
export function yarnTexture(): CanvasTexture {
  return cached('yarn', () => {
    const size = 256
    const [element, context] = canvas(size)
    const next = random(11)
    context.fillStyle = '#d9d9d9'
    context.fillRect(0, 0, size, size)
    for (let i = 0; i < 650; i++) {
      const y = next() * size
      const drift = (next() - 0.5) * 40
      const light = next() > 0.45
      context.strokeStyle = light ? `rgba(255,255,255,${0.25 + next() * 0.4})` : `rgba(0,0,0,${0.05 + next() * 0.12})`
      context.lineWidth = 0.5 + next() * 1.3
      for (const offset of [-size, 0, size]) {
        context.beginPath()
        context.moveTo(0, y + offset)
        context.lineTo(size, y + drift + offset)
        context.stroke()
      }
    }
    return texture(element, 4, 3)
  })
}

/** A plain weave: warp and weft threads passing over and under each other. Light grey for tinting. */
export function weaveTexture(): CanvasTexture {
  return cached('weave', () => {
    const size = 128
    const cell = 8
    const [element, context] = canvas(size)
    const next = random(23)
    context.fillStyle = '#bdbdbd'
    context.fillRect(0, 0, size, size)
    for (let row = 0; row < size / cell; row++) {
      for (let column = 0; column < size / cell; column++) {
        const x = column * cell
        const y = row * cell
        const warpOnTop = (row + column) % 2 === 0
        const shade = 205 + Math.floor(next() * 35)
        const gradient = warpOnTop ? context.createLinearGradient(x, 0, x + cell, 0) : context.createLinearGradient(0, y, 0, y + cell)
        gradient.addColorStop(0, `rgb(${shade - 45},${shade - 45},${shade - 45})`)
        gradient.addColorStop(0.5, `rgb(${shade},${shade},${shade})`)
        gradient.addColorStop(1, `rgb(${shade - 45},${shade - 45},${shade - 45})`)
        context.fillStyle = gradient
        if (warpOnTop) context.fillRect(x + 1, y, cell - 2, cell)
        else context.fillRect(x, y + 1, cell, cell - 2)
      }
    }
    return texture(element, 6, 6)
  })
}

/** The end of a fabric roll: the cloth's layers as rings around the cardboard core. */
export function rollEndTexture(): CanvasTexture {
  return cached('roll-end', () => {
    const size = 256
    const [element, context] = canvas(size)
    const centre = size / 2
    context.fillStyle = '#cfcfcf'
    context.fillRect(0, 0, size, size)
    for (let radius = 22; radius < centre; radius += 3.2) {
      context.strokeStyle = `rgba(0,0,0,${0.08 + (radius % 7) * 0.012})`
      context.lineWidth = 1.2
      context.beginPath()
      context.arc(centre, centre, radius, 0, Math.PI * 2)
      context.stroke()
    }
    context.fillStyle = '#8a6a48'
    context.beginPath()
    context.arc(centre, centre, 20, 0, Math.PI * 2)
    context.fill()
    context.fillStyle = '#3a2a1c'
    context.beginPath()
    context.arc(centre, centre, 12, 0, Math.PI * 2)
    context.fill()
    const result = texture(element, 1, 1)
    result.wrapS = result.wrapT = ClampToEdgeWrapping
    return result
  })
}

/** A text label drawn onto a texture, for month names and values in the 3D chart. */
export function labelTexture(text: string, { color = '#475069', weight = 600, size = 46 } = {}): CanvasTexture {
  return cached(`label:${text}:${color}:${weight}:${size}`, () => {
    const [element, context] = canvas(256)
    element.height = 64
    context.clearRect(0, 0, 256, 64)
    context.font = `${weight} ${size}px 'Geist Variable', system-ui, sans-serif`
    context.fillStyle = color
    context.textAlign = 'center'
    context.textBaseline = 'middle'
    context.fillText(text, 128, 34)
    const result = new CanvasTexture(element)
    result.colorSpace = SRGBColorSpace
    result.anisotropy = 4
    return result
  })
}
