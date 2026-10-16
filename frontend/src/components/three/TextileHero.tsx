import { useThree } from '@react-three/fiber'
import type { ReactNode } from 'react'
import { MathUtils } from 'three'
import { SceneCanvas } from './SceneCanvas'
import { FabricRoll, Fibres, Floating, FoldedStack, PointerRig, StudioLights, YarnCone } from './models'
import { palette } from './palette'

/** Scales the scene to the panel, so a narrow window doesn't push the objects over the headline. */
function FitToPanel({ children }: { children: ReactNode }) {
  const width = useThree((state) => state.viewport.width)
  return <group scale={MathUtils.clamp(width / 4.4, 0.62, 1.25)}>{children}</group>
}

/**
 * The sign-in page's scene: yarn cones, a roll of cloth unwinding and a stack of folded linen floating in soft
 * light around the headline (the left middle stays clear for it), leaning towards the pointer, with fibres
 * drifting up through it.
 */
export default function TextileHero({ className }: { className?: string }) {
  return (
    <SceneCanvas className={className} camera={{ position: [0, 0, 8], fov: 36 }}>
      <StudioLights />
      <FitToPanel>
        <PointerRig strength={0.3}>
          <Floating position={[1.2, 1.45, 0]} phase={0} spin={0.3}>
            <YarnCone color={palette.ecru} rotation={[0.3, 0, -0.2]} />
          </Floating>
          <Floating position={[0.05, 2.05, -1.6]} phase={4.2} spin={0.35} amplitude={0.1}>
            <YarnCone color={palette.saffron} scale={0.55} rotation={[0.4, 0, 0.2]} />
          </Floating>
          <Floating position={[-1.3, 1.3, -0.8]} phase={5} spin={0.12} amplitude={0.08}>
            <FoldedStack rotation={[0.4, 0.6, 0]} scale={0.5} />
          </Floating>
          <Floating position={[1.8, 0.2, 0.7]} phase={1.6} spin={-0.25} amplitude={0.13}>
            <YarnCone color={palette.indigo} scale={0.72} rotation={[-0.25, 0, 0.35]} />
          </Floating>
          <Floating position={[1.25, -1.05, -0.5]} phase={2.2} spin={0} amplitude={0.07}>
            <FabricRoll color={palette.denim} rotation={[0.2, -0.55, 0.06]} scale={0.8} />
          </Floating>
          <Floating position={[-0.35, -2.05, 0.4]} phase={3.1} spin={0.2}>
            <YarnCone color={palette.teal} scale={0.62} rotation={[0.15, 0, 0.5]} />
          </Floating>
        </PointerRig>
      </FitToPanel>
      <Fibres count={240} width={9} height={6} />
    </SceneCanvas>
  )
}
