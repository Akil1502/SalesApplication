import { useFrame, useThree, type ThreeElements } from '@react-three/fiber'
import { useEffect, useMemo, useRef, type ReactNode } from 'react'
import {
  CircleGeometry,
  CylinderGeometry,
  DoubleSide,
  Group,
  LatheGeometry,
  MathUtils,
  PlaneGeometry,
  PMREMGenerator,
  Points,
  Vector2,
} from 'three'
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js'
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js'
import { palette } from './palette'
import type { ProductModel } from './product-models'
import { random, rollEndTexture, weaveTexture, yarnTexture } from './textures'

type GroupProps = ThreeElements['group']

/** Soft studio light: a room reflected in every surface plus a key light. Built in code, nothing downloaded. */
export function StudioLights({ intensity = 1 }: { intensity?: number }) {
  const gl = useThree((state) => state.gl)
  const scene = useThree((state) => state.scene)

  useEffect(() => {
    const generator = new PMREMGenerator(gl)
    const room = new RoomEnvironment()
    const environment = generator.fromScene(room, 0.04).texture
    scene.environment = environment
    return () => {
      scene.environment = null
      environment.dispose()
      generator.dispose()
      room.dispose()
    }
  }, [gl, scene])

  return (
    <>
      <ambientLight intensity={0.3 * intensity} />
      <directionalLight position={[4, 6, 5]} intensity={1.5 * intensity} />
      <directionalLight position={[-5, 2, -3]} intensity={0.45 * intensity} color="#9db8ff" />
    </>
  )
}

/** Bobs gently up and down and turns slowly, each object out of step with the others. */
export function Floating({
  children,
  position = [0, 0, 0],
  speed = 1,
  amplitude = 0.12,
  spin = 0.2,
  phase = 0,
  ...props
}: Omit<GroupProps, 'position'> & { position?: [number, number, number]; speed?: number; amplitude?: number; spin?: number; phase?: number }) {
  const ref = useRef<Group>(null)
  useFrame((state, delta) => {
    const group = ref.current
    if (!group) return
    const t = state.clock.elapsedTime * speed + phase
    group.position.y = position[1] + Math.sin(t) * amplitude
    group.rotation.y += delta * spin
    group.rotation.z = Math.sin(t * 0.7) * 0.05
  })
  return (
    <group ref={ref} position={position} {...props}>
      {children}
    </group>
  )
}

/** Leans its contents towards the pointer. */
export function PointerRig({ children, strength = 0.25 }: { children: ReactNode; strength?: number }) {
  const ref = useRef<Group>(null)
  useFrame((state) => {
    const group = ref.current
    if (!group) return
    group.rotation.y = MathUtils.lerp(group.rotation.y, state.pointer.x * strength, 0.05)
    group.rotation.x = MathUtils.lerp(group.rotation.x, -state.pointer.y * strength * 0.5, 0.05)
  })
  return <group ref={ref}>{children}</group>
}

// Shapes are built once and shared by every cone, roll and stack.
const shapes: { yarn?: LatheGeometry; core?: CylinderGeometry; fold?: RoundedBoxGeometry; carton?: RoundedBoxGeometry; shadow?: CircleGeometry } = {}

/** The side of a yarn cone, turned like a lathe: a tapering package with rounded shoulders. */
function yarnShape() {
  shapes.yarn ??= new LatheGeometry(
    [
      [0.17, -0.5],
      [0.5, -0.5],
      [0.57, -0.46],
      [0.585, -0.4],
      [0.39, 0.42],
      [0.355, 0.48],
      [0.3, 0.505],
      [0.16, 0.505],
    ].map(([x, y]) => new Vector2(x, y)),
    72,
  )
  return shapes.yarn
}

export function YarnCone({ color = palette.ecru, ...props }: GroupProps & { color?: string }) {
  const threads = yarnTexture()
  shapes.core ??= new CylinderGeometry(0.11, 0.17, 1.32, 40, 1, true)
  return (
    <group {...props}>
      <mesh geometry={yarnShape()}>
        <meshStandardMaterial color={color} map={threads} bumpMap={threads} bumpScale={0.8} roughness={0.82} side={DoubleSide} />
      </mesh>
      <mesh geometry={shapes.core} position={[0, 0.06, 0]}>
        <meshStandardMaterial color="#c49f74" roughness={0.55} side={DoubleSide} />
      </mesh>
    </group>
  )
}

/** A roll of cloth on a cardboard core, with a length of it hanging down and rippling. */
export function FabricRoll({
  color = palette.denim,
  length = 1.7,
  radius = 0.36,
  drape = true,
  ...props
}: GroupProps & { color?: string; length?: number; radius?: number; drape?: boolean }) {
  const weave = weaveTexture()
  const ends = rollEndTexture()
  const cloth = useMemo(() => new PlaneGeometry(length * 0.96, 1.1, 36, 24), [length])
  const rest = useMemo(() => Float32Array.from(cloth.attributes.position!.array), [cloth])
  useEffect(() => () => cloth.dispose(), [cloth])

  useFrame(({ clock }) => {
    if (!drape) return
    const t = clock.elapsedTime
    const position = cloth.attributes.position!
    for (let i = 0; i < position.count; i++) {
      const x = rest[i * 3]!
      const y = rest[i * 3 + 1]!
      const fall = (0.55 - y) / 1.1
      position.setZ(i, (Math.sin(x * 2.6 + t * 1.4) * 0.08 + Math.sin(y * 4 + t * 2) * 0.04) * fall)
    }
    position.needsUpdate = true
    cloth.computeVertexNormals()
  })

  return (
    <group {...props}>
      <mesh rotation={[0, 0, Math.PI / 2]}>
        <cylinderGeometry args={[radius, radius, length, 64]} />
        <meshStandardMaterial attach="material-0" color={color} map={weave} bumpMap={weave} bumpScale={0.5} roughness={0.9} />
        <meshStandardMaterial attach="material-1" color={color} map={ends} roughness={0.8} />
        <meshStandardMaterial attach="material-2" color={color} map={ends} roughness={0.8} />
      </mesh>
      {drape && (
        <mesh geometry={cloth} position={[0, -0.55, radius * 0.98]} rotation={[-0.18, 0, 0]}>
          <meshStandardMaterial color={color} map={weave} bumpMap={weave} bumpScale={0.4} roughness={0.92} side={DoubleSide} />
        </mesh>
      )}
    </group>
  )
}

/** Folded sheets or garments stacked a little unevenly. */
export function FoldedStack({
  colors = [palette.white, palette.sky, palette.indigo, palette.rose],
  ...props
}: GroupProps & { colors?: readonly string[] }) {
  const weave = weaveTexture()
  shapes.fold ??= new RoundedBoxGeometry(1.3, 0.17, 0.95, 4, 0.07)
  return (
    <group {...props}>
      {colors.map((color, i) => (
        <mesh key={i} geometry={shapes.fold} position={[i % 2 ? 0.035 : -0.035, i * 0.175, 0]} rotation={[0, (i - colors.length / 2) * 0.05, 0]}>
          <meshStandardMaterial color={color} map={weave} bumpMap={weave} bumpScale={0.35} roughness={0.95} />
        </mesh>
      ))}
    </group>
  )
}

/** A taped carton, for packing material. */
export function Carton(props: GroupProps) {
  shapes.carton ??= new RoundedBoxGeometry(1.2, 0.85, 0.9, 4, 0.05)
  return (
    <group {...props}>
      <mesh geometry={shapes.carton}>
        <meshStandardMaterial color={palette.kraft} roughness={0.85} />
      </mesh>
      <mesh position={[0, 0.426, 0]} rotation={[-Math.PI / 2, 0, 0]}>
        <planeGeometry args={[0.22, 0.92]} />
        <meshStandardMaterial color="#d8c7a8" roughness={0.4} transparent opacity={0.85} />
      </mesh>
    </group>
  )
}

/** A soft round shadow on the floor, under a floating object. */
export function SoftShadow({ radius = 1, opacity = 0.18, ...props }: GroupProps & { radius?: number; opacity?: number }) {
  shapes.shadow ??= new CircleGeometry(1, 48)
  return (
    <group {...props}>
      <mesh geometry={shapes.shadow} rotation={[-Math.PI / 2, 0, 0]} scale={radius}>
        <meshBasicMaterial color="#0b1030" transparent opacity={opacity} depthWrite={false} />
      </mesh>
    </group>
  )
}

/** Fine fibres drifting upward through the scene, like lint in a mill's light. */
export function Fibres({ count = 220, width = 9, height = 6, depth = 4, color = '#ffffff', opacity = 0.5 }) {
  const ref = useRef<Points>(null)
  const [positions, speeds] = useMemo(() => {
    const next = random(97)
    const points = new Float32Array(count * 3)
    const rates = new Float32Array(count)
    for (let i = 0; i < count; i++) {
      points[i * 3] = (next() - 0.5) * width
      points[i * 3 + 1] = (next() - 0.5) * height
      points[i * 3 + 2] = (next() - 0.5) * depth
      rates[i] = 0.08 + next() * 0.22
    }
    return [points, rates]
  }, [count, width, height, depth])

  useFrame((_, delta) => {
    const attribute = ref.current?.geometry.attributes.position
    if (!attribute) return
    for (let i = 0; i < count; i++) {
      let y = attribute.getY(i) + speeds[i]! * delta
      if (y > height / 2) y = -height / 2
      attribute.setY(i, y)
    }
    attribute.needsUpdate = true
  })

  return (
    <points ref={ref}>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[positions, 3]} />
      </bufferGeometry>
      <pointsMaterial size={0.035} color={color} transparent opacity={opacity} sizeAttenuation depthWrite={false} />
    </points>
  )
}

export function ProductModelMesh({ model, color }: { model: ProductModel; color?: string }) {
  switch (model) {
    case 'yarn':
      return <YarnCone color={color ?? palette.ecru} scale={1.5} />
    case 'fabric':
      return <FabricRoll color={color ?? palette.denim} scale={1.1} />
    case 'carton':
      return <Carton scale={1.3} />
    default:
      return <FoldedStack scale={1.2} position={[0, -0.3, 0]} />
  }
}
