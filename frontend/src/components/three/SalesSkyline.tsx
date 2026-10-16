import { useFrame, useThree } from '@react-three/fiber'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Color, Group, MathUtils } from 'three'
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js'
import { SceneCanvas } from './SceneCanvas'
import { SoftShadow, StudioLights } from './models'
import { palette } from './palette'
import { labelTexture } from './textures'

export interface SkylineMonth {
  /** Short label under the column, e.g. "Oct ’26". */
  tick: string
  value: number
  partial: boolean
}

interface SkylineProps {
  months: SkylineMonth[]
  dark: boolean
  formatValue: (value: number) => string
  /** The column under the pointer, for a caption outside the canvas. */
  onHover?: (index: number | null) => void
  className?: string
}

const low = new Color(palette.indigo)
const high = new Color(palette.teal)

const easeOutBack = (t: number) => 1 + 2.2 * Math.pow(t - 1, 3) + 1.2 * Math.pow(t - 1, 2)

function Column({
  index,
  x,
  width,
  height,
  share,
  partial,
  hovered,
  onOver,
  onOut,
}: {
  index: number
  x: number
  width: number
  height: number
  share: number
  partial: boolean
  hovered: boolean
  onOver: (index: number) => void
  onOut: () => void
}) {
  const group = useRef<Group>(null)
  const born = useRef<number | null>(null)
  const geometry = useMemo(() => new RoundedBoxGeometry(width, height, width, 3, Math.min(0.05, width / 5)).translate(0, height / 2, 0), [width, height])
  useEffect(() => () => geometry.dispose(), [geometry])
  const color = useMemo(() => low.clone().lerp(high, share), [share])

  useFrame(({ clock }) => {
    const column = group.current
    if (!column) return
    born.current ??= clock.elapsedTime
    // Columns grow out of the floor one after another, overshooting a little.
    const t = MathUtils.clamp((clock.elapsedTime - born.current - index * 0.045) / 0.9, 0, 1)
    column.scale.y = Math.max(0.001, easeOutBack(t))
    column.position.y = MathUtils.lerp(column.position.y, hovered ? 0.12 : 0, 0.18)
  })

  return (
    <group ref={group} position={[x, 0, 0]}>
      <mesh
        geometry={geometry}
        onPointerOver={(event) => {
          event.stopPropagation()
          onOver(index)
        }}
        onPointerOut={onOut}
      >
        <meshPhysicalMaterial
          color={color}
          emissive={color}
          emissiveIntensity={hovered ? 0.45 : 0.06}
          roughness={0.3}
          metalness={0.15}
          clearcoat={0.7}
          clearcoatRoughness={0.2}
          transparent={partial}
          opacity={partial ? 0.5 : 1}
        />
      </mesh>
    </group>
  )
}

function Label({ text, position, color, scale = 0.9 }: { text: string; position: [number, number, number]; color: string; scale?: number }) {
  const map = labelTexture(text, { color })
  return (
    <sprite position={position} scale={[scale, scale / 4, 1]}>
      <spriteMaterial map={map} transparent depthWrite={false} />
    </sprite>
  )
}

function Skyline({ months, dark, formatValue, onHover }: Omit<SkylineProps, 'className'>) {
  const [hovered, setHovered] = useState<number | null>(null)
  const scene = useRef<Group>(null)
  const camera = useThree((state) => state.camera)
  useEffect(() => camera.lookAt(0, 0.9, 0), [camera])

  const count = months.length
  const spacing = Math.min(0.62, 7.2 / Math.max(count, 1))
  const width = spacing * 0.7
  const max = Math.max(...months.map((m) => m.value), 1)
  const labelEvery = Math.ceil(count / 8)
  const labelColor = dark ? '#aab3cf' : '#5b6380'
  const valueColor = dark ? '#ffffff' : '#1f2a5c'

  const hover = (index: number | null) => {
    setHovered(index)
    onHover?.(index)
    document.body.style.cursor = index === null ? '' : 'pointer'
  }
  useEffect(() => () => void (document.body.style.cursor = ''), [])

  useFrame((state) => {
    const group = scene.current
    if (!group) return
    const drift = Math.sin(state.clock.elapsedTime * 0.25) * 0.16
    group.rotation.y = MathUtils.lerp(group.rotation.y, drift + state.pointer.x * 0.3, 0.04)
    group.rotation.x = MathUtils.lerp(group.rotation.x, state.pointer.y * -0.06, 0.04)
  })

  const heightOf = (value: number) => 0.06 + (value / max) * 2.5
  const xOf = (i: number) => (i - (count - 1) / 2) * spacing
  const active = hovered === null ? null : months[hovered]

  return (
    <group ref={scene}>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -0.002, 0]}>
        <planeGeometry args={[count * spacing + 1.2, 2.4]} />
        <meshStandardMaterial color={dark ? '#1a2142' : '#e9ecf7'} roughness={1} transparent opacity={dark ? 0.7 : 0.85} />
      </mesh>
      {months.map((month, i) => (
        <group key={month.tick + i}>
          <SoftShadow position={[xOf(i), 0, 0]} radius={width * 0.9} opacity={0.12} />
          <Column
            index={i}
            x={xOf(i)}
            width={width}
            height={heightOf(month.value)}
            share={month.value / max}
            partial={month.partial}
            hovered={hovered === i}
            onOver={hover}
            onOut={() => hover(null)}
          />
          {(i % labelEvery === (count - 1) % labelEvery || hovered === i) && (
            <Label text={month.tick} position={[xOf(i), 0.05, width + 0.35]} color={hovered === i ? valueColor : labelColor} scale={1.05} />
          )}
        </group>
      ))}
      {active && hovered !== null && (
        <Label text={formatValue(active.value)} position={[xOf(hovered), heightOf(active.value) + 0.42, 0]} color={valueColor} scale={1.3} />
      )}
    </group>
  )
}

/** Monthly sales as a row of glossy 3D columns that grow out of the floor and lift when pointed at. */
export default function SalesSkyline({ className, ...props }: SkylineProps) {
  return (
    <SceneCanvas className={className} camera={{ position: [0, 2.8, 7.2], fov: 34 }}>
      <StudioLights intensity={0.9} />
      <Skyline {...props} />
    </SceneCanvas>
  )
}
