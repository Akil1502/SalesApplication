import { useFrame } from '@react-three/fiber'
import { useEffect, useMemo, useRef } from 'react'
import { Group, IcosahedronGeometry, MathUtils, Mesh, MeshPhysicalMaterial } from 'three'
import { mergeVertices } from 'three/examples/jsm/utils/BufferGeometryUtils.js'
import { SceneCanvas } from './SceneCanvas'
import { StudioLights } from './models'

/** A smooth ball whose surface ripples: slowly at rest, faster and deeper while the AI is working. */
function Blob({ active }: { active: boolean }) {
  const mesh = useRef<Mesh>(null)
  const material = useRef<MeshPhysicalMaterial>(null)
  const energy = useRef(0.3)
  const geometry = useMemo(() => {
    // Welded, so the surface stays smooth as it moves.
    const shape = new IcosahedronGeometry(1, 20)
    shape.deleteAttribute('normal')
    shape.deleteAttribute('uv')
    return mergeVertices(shape)
  }, [])
  const rest = useMemo(() => Float32Array.from(geometry.attributes.position!.array), [geometry])
  useEffect(() => () => geometry.dispose(), [geometry])

  useFrame(({ clock }, delta) => {
    energy.current = MathUtils.lerp(energy.current, active ? 1 : 0.3, 0.04)
    const e = energy.current
    const t = clock.elapsedTime * (0.5 + e * 1.1)
    const depth = 0.05 + e * 0.11
    const position = geometry.attributes.position!
    for (let i = 0; i < position.count; i++) {
      const x = rest[i * 3]!
      const y = rest[i * 3 + 1]!
      const z = rest[i * 3 + 2]!
      const wave = Math.sin(x * 2.2 + t) * Math.sin(y * 2.5 + t * 1.3) * Math.sin(z * 1.9 + t * 0.8)
      const scale = 1 + wave * depth * 2.4
      position.setXYZ(i, x * scale, y * scale, z * scale)
    }
    position.needsUpdate = true
    geometry.computeVertexNormals()
    if (mesh.current) mesh.current.rotation.y += delta * (0.15 + e * 0.4)
    if (material.current) material.current.emissiveIntensity = 0.12 + e * 0.4
  })

  return (
    <mesh ref={mesh} geometry={geometry}>
      <meshPhysicalMaterial
        ref={material}
        color="#5a5ff0"
        emissive="#2aa3c0"
        roughness={0.18}
        metalness={0.1}
        clearcoat={1}
        clearcoatRoughness={0.15}
        iridescence={0.9}
        iridescenceIOR={1.35}
      />
    </mesh>
  )
}

/** Small lights circling the ball on tilted paths, faster while it works. */
function Orbits({ active }: { active: boolean }) {
  const group = useRef<Group>(null)
  const speed = useRef(0.4)
  useFrame((_, delta) => {
    speed.current = MathUtils.lerp(speed.current, active ? 1.8 : 0.4, 0.04)
    group.current?.children.forEach((ring, i) => {
      ring.rotation.y += delta * speed.current * (1 + i * 0.35)
    })
  })
  return (
    <group ref={group}>
      {[
        { tilt: 0.5, radius: 1.55, color: '#7fd7e6' },
        { tilt: -0.8, radius: 1.75, color: '#a5a8ff' },
        { tilt: 1.3, radius: 1.4, color: '#ffffff' },
      ].map((ring, i) => (
        <group key={i} rotation={[ring.tilt, 0, ring.tilt / 2]}>
          <mesh position={[ring.radius, 0, 0]}>
            <sphereGeometry args={[0.055, 16, 16]} />
            <meshBasicMaterial color={ring.color} />
          </mesh>
        </group>
      ))}
    </group>
  )
}

/** The AI's "thinking" ball for Ask your data, smart order entry and insights. */
export default function AiOrb({ active = false, className }: { active?: boolean; className?: string }) {
  return (
    <SceneCanvas className={className} camera={{ position: [0, 0, 4.6], fov: 35 }}>
      <StudioLights intensity={0.8} />
      <Blob active={active} />
      <Orbits active={active} />
    </SceneCanvas>
  )
}
