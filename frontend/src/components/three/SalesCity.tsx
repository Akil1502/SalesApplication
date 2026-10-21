import { useFrame, useThree } from '@react-three/fiber'
import { useEffect, useMemo, useRef, useState } from 'react'
import { BoxGeometry, CanvasTexture, Color, Group, MathUtils, Mesh, RepeatWrapping, SRGBColorSpace } from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { useSceneColours } from '@/features/branding/apply'
import { SceneCanvas } from './SceneCanvas'
import { Fibres, SoftShadow, StudioLights } from './models'
import { palette } from './palette'
import { labelTexture, random } from './textures'

export interface CityMonth {
  /** Short label, e.g. "Oct ’26". */
  tick: string
  value: number
  partial: boolean
}

interface CityProps {
  months: CityMonth[]
  dark: boolean
  formatValue: (value: number) => string
  /** The building under the pointer, for a caption outside the canvas. */
  onHover?: (index: number | null) => void
  className?: string
}

const STANDARD_COLOURS: [string, string] = [palette.indigo, palette.teal]

/** The ground, road and night fog, tinted towards the company's colour when it has chosen one. */
function groundColours(dark: boolean, brand: string | undefined) {
  const tinted = (standard: string, neutral: string, amount: number) =>
    brand ? `#${new Color(neutral).lerp(new Color(brand), amount).getHexString()}` : standard
  return dark
    ? { ground: tinted('#151b38', '#14141a', 0.22), road: tinted('#0d1128', '#0c0c10', 0.18), fog: tinted('#10152e', '#101014', 0.2) }
    : { ground: tinted('#e6eaf6', '#ececee', 0.08), road: tinted('#cfd5ea', '#d4d4d8', 0.12), fog: '#ffffff' }
}
const PER_ROW = 12
const SPACING = 0.78
const ROW_GAP = 2.1

let facadeImages: { facade: HTMLCanvasElement; glow: HTMLCanvasElement } | undefined

/**
 * A facade of windows, some lit, some dark, and its glow: only the lit windows, so at night the windows shine and
 * the walls stay dark. Shared by every building; each repeats them to its own height.
 */
function facadeImagesOnce() {
  if (facadeImages) return facadeImages
  const canvas = () => {
    const element = document.createElement('canvas')
    element.width = element.height = 64
    return [element, element.getContext('2d')!] as const
  }
  const [facade, wall] = canvas()
  const [glow, light] = canvas()
  const next = random(5)
  wall.fillStyle = '#8f97b3'
  wall.fillRect(0, 0, 64, 64)
  light.fillStyle = '#000000'
  light.fillRect(0, 0, 64, 64)
  for (let row = 0; row < 4; row++) {
    for (let column = 0; column < 4; column++) {
      const lit = next() < 0.55
      const colour = next() < 0.7 ? '#ffe2a3' : '#bfefff'
      wall.fillStyle = lit ? colour : '#2b3150'
      wall.fillRect(4 + column * 15, 4 + row * 15, 10, 9)
      if (lit) {
        light.fillStyle = colour
        light.fillRect(4 + column * 15, 4 + row * 15, 10, 9)
      }
    }
  }
  facadeImages = { facade, glow }
  return facadeImages
}

function facadeTexture(image: HTMLCanvasElement, height: number) {
  const texture = new CanvasTexture(image)
  texture.wrapS = texture.wrapT = RepeatWrapping
  texture.colorSpace = SRGBColorSpace
  texture.repeat.set(1, Math.max(1, Math.round(height * 1.6)))
  return texture
}

const box = new BoxGeometry(1, 1, 1).translate(0, 0.5, 0)

function Building({
  index,
  position,
  width,
  height,
  share,
  colours,
  partial,
  dark,
  hovered,
  onOver,
  onOut,
}: {
  index: number
  position: [number, number, number]
  width: number
  height: number
  share: number
  /** The smallest month's colour and the biggest's: the brand colour and its second. */
  colours: [string, string]
  partial: boolean
  dark: boolean
  hovered: boolean
  onOver: (index: number) => void
  onOut: () => void
}) {
  const group = useRef<Group>(null)
  const born = useRef<number | null>(null)
  const [facade, glow] = useMemo(() => {
    const images = facadeImagesOnce()
    return [facadeTexture(images.facade, height), facadeTexture(images.glow, height)]
  }, [height])
  useEffect(
    () => () => {
      facade.dispose()
      glow.dispose()
    },
    [facade, glow],
  )
  const colour = useMemo(() => new Color(colours[0]).lerp(new Color(colours[1]), share), [colours, share])

  useFrame(({ clock }) => {
    const building = group.current
    if (!building) return
    born.current ??= clock.elapsedTime
    // Buildings rise out of the ground one after another.
    const t = MathUtils.clamp((clock.elapsedTime - born.current - index * 0.04) / 1.1, 0, 1)
    building.scale.y = Math.max(0.001, 1 - Math.pow(1 - t, 3))
  })

  return (
    <group ref={group} position={position}>
      <mesh
        geometry={box}
        scale={[width, height, width]}
        onPointerOver={(event) => {
          event.stopPropagation()
          onOver(index)
        }}
        onPointerOut={onOut}
      >
        <meshStandardMaterial
          color={colour}
          map={facade}
          emissiveMap={glow}
          emissive="#ffffff"
          emissiveIntensity={hovered ? 1.8 : dark ? 1.1 : 0.2}
          roughness={0.35}
          metalness={0.25}
          transparent={partial}
          opacity={partial ? 0.55 : 1}
        />
      </mesh>
      {/* A glowing roof light, brighter on the building you point at. */}
      <mesh geometry={box} position={[0, height, 0]} scale={[width * 0.92, 0.05, width * 0.92]}>
        <meshStandardMaterial color={palette.teal} emissive={palette.teal} emissiveIntensity={hovered ? 3 : dark ? 1.2 : 0.5} />
      </mesh>
    </group>
  )
}

function Label({ text, position, color, scale = 1 }: { text: string; position: [number, number, number]; color: string; scale?: number }) {
  return (
    <sprite position={position} scale={[scale, scale / 4, 1]}>
      <spriteMaterial map={labelTexture(text, { color })} transparent depthWrite={false} />
    </sprite>
  )
}

/** Little lights driving along the road between the rows. */
function Traffic({ length, dark }: { length: number; dark: boolean }) {
  const cars = useRef<(Mesh | null)[]>([])
  const lanes = useMemo(() => {
    const next = random(31)
    return Array.from({ length: 6 }, (_, i) => ({ offset: next() * length, speed: 0.6 + next() * 0.8, lane: i % 2 === 0 ? 0.18 : -0.18, colour: i % 2 === 0 ? '#ffe9b0' : '#ff6b6b' }))
  }, [length])
  useFrame(({ clock }) => {
    lanes.forEach((car, i) => {
      const mesh = cars.current[i]
      if (!mesh) return
      const travelled = (car.offset + clock.elapsedTime * car.speed) % length
      mesh.position.x = car.lane > 0 ? travelled - length / 2 : length / 2 - travelled
    })
  })
  return (
    <group position={[0, 0.04, 0]}>
      {lanes.map((car, i) => (
        <mesh key={i} ref={(mesh) => void (cars.current[i] = mesh)} position={[0, 0, car.lane]}>
          <sphereGeometry args={[0.045, 10, 10]} />
          <meshBasicMaterial color={car.colour} transparent opacity={dark ? 1 : 0.8} />
        </mesh>
      ))}
    </group>
  )
}

/** Drag to turn the city (with a mouse); it also turns slowly by itself. No zoom, so the page still scrolls. */
function Orbit() {
  const camera = useThree((state) => state.camera)
  const element = useThree((state) => state.gl.domElement)
  const controls = useMemo(() => new OrbitControls(camera, element), [camera, element])

  useEffect(() => {
    controls.target.set(0, 0.7, 0)
    controls.enableZoom = false
    controls.enablePan = false
    controls.enableDamping = true
    controls.dampingFactor = 0.06
    controls.autoRotate = true
    controls.autoRotateSpeed = 0.55
    controls.minPolarAngle = 0.55
    controls.maxPolarAngle = 1.3
    // On touch screens dragging would trap the page's scrolling, so the city only turns by itself there.
    if (window.matchMedia('(pointer: coarse)').matches) {
      controls.enabled = false
      element.style.touchAction = 'auto'
    }
    return () => controls.dispose()
  }, [controls, element])

  useFrame(() => controls.update())
  return null
}

function City({ months, dark, formatValue, onHover }: Omit<CityProps, 'className'>) {
  const [hovered, setHovered] = useState<number | null>(null)
  const rows = Math.max(1, Math.ceil(months.length / PER_ROW))
  const max = Math.max(...months.map((m) => m.value), 1)
  const width = SPACING * 0.66
  const length = PER_ROW * SPACING + 1.6
  const labelColour = dark ? '#aab3cf' : '#5b6380'
  const valueColour = dark ? '#ffffff' : '#1f2a5c'
  const brand = useSceneColours(false)
  const colours = brand ?? STANDARD_COLOURS
  const surface = groundColours(dark, brand?.[0])

  const placeOf = (i: number): [number, number, number] => {
    const row = Math.floor(i / PER_ROW)
    const column = i % PER_ROW
    // The newest year is the front row.
    const z = (row - (rows - 1) / 2) * ROW_GAP
    return [(column - (PER_ROW - 1) / 2) * SPACING, 0, z]
  }
  const heightOf = (value: number) => 0.12 + (value / max) * 2.9

  const hover = (index: number | null) => {
    setHovered(index)
    onHover?.(index)
    document.body.style.cursor = index === null ? '' : 'pointer'
  }
  useEffect(() => () => void (document.body.style.cursor = ''), [])

  const active = hovered === null ? undefined : months[hovered]

  return (
    <group>
      {/* The ground, and the road between the years. */}
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -0.01, 0]}>
        <planeGeometry args={[length + 2, rows * ROW_GAP + 2.4]} />
        <meshStandardMaterial color={surface.ground} roughness={1} />
      </mesh>
      {rows > 1 && (
        <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0, 0]}>
          <planeGeometry args={[length, 0.7]} />
          <meshStandardMaterial color={surface.road} roughness={0.9} />
        </mesh>
      )}
      {rows > 1 && <Traffic length={length} dark={dark} />}

      {months.map((month, i) => {
        const [x, , z] = placeOf(i)
        const front = Math.floor(i / PER_ROW) === rows - 1
        return (
          <group key={`${month.tick}-${i}`}>
            <SoftShadow position={[x, 0.001, z]} radius={width * 0.95} opacity={0.14} />
            <Building
              index={i}
              position={[x, 0, z]}
              width={width}
              height={heightOf(month.value)}
              share={month.value / max}
              colours={colours}
              partial={month.partial}
              dark={dark}
              hovered={hovered === i}
              onOver={hover}
              onOut={() => hover(null)}
            />
            {front && (i % 3 === (months.length - 1) % 3 || hovered === i) && (
              <Label text={month.tick} position={[x, 0.06, z + width + 0.35]} color={hovered === i ? valueColour : labelColour} scale={1.1} />
            )}
          </group>
        )
      })}

      {active && hovered !== null && (
        <Label text={formatValue(active.value)} position={[placeOf(hovered)[0], heightOf(active.value) + 0.5, placeOf(hovered)[2]]} color={valueColour} scale={1.5} />
      )}
    </group>
  )
}

/**
 * The dashboard's hero: each month's sales as a building, the last two years as two streets of a small city that
 * turns slowly and can be dragged around. Point at a building for its month.
 */
export default function SalesCity({ className, ...props }: CityProps) {
  const brand = useSceneColours(false)
  return (
    <SceneCanvas className={className} camera={{ position: [7.5, 5.2, 9.5], fov: 34 }}>
      {props.dark && <fog attach="fog" args={[groundColours(true, brand?.[0]).fog, 13, 30]} />}
      <StudioLights intensity={props.dark ? 0.55 : 0.95} />
      <City {...props} />
      <Fibres count={120} width={14} height={5} depth={8} opacity={props.dark ? 0.6 : 0.35} color={props.dark ? '#bfefff' : '#ffffff'} />
      <Orbit />
    </SceneCanvas>
  )
}
