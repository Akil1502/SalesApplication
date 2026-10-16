import { useLoader } from '@react-three/fiber'
import { Suspense, useEffect, useMemo } from 'react'
import { SRGBColorSpace, TextureLoader } from 'three'
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js'
import { SceneCanvas } from './SceneCanvas'
import { Floating, PointerRig, ProductModelMesh, SoftShadow, StudioLights } from './models'
import type { ProductModel } from './product-models'

/** The product's photo, mounted on a thin glossy card. */
function PhotoCard({ url }: { url: string }) {
  const photo = useLoader(TextureLoader, url)
  photo.colorSpace = SRGBColorSpace
  const image = photo.image as { width: number; height: number }
  const aspect = image.width / image.height
  const width = aspect >= 1 ? 2.3 : 2.3 * aspect
  const height = width / aspect
  const frame = useMemo(() => new RoundedBoxGeometry(width + 0.12, height + 0.12, 0.08, 4, 0.06), [width, height])
  useEffect(() => () => frame.dispose(), [frame])

  return (
    <group>
      <mesh geometry={frame}>
        <meshPhysicalMaterial color="#f7f7fb" roughness={0.25} clearcoat={1} clearcoatRoughness={0.1} />
      </mesh>
      <mesh position={[0, 0, 0.041]}>
        <planeGeometry args={[width, height]} />
        {/* Unlit, so the photo keeps its true colours instead of taking on the scene's lighting. */}
        <meshBasicMaterial map={photo} toneMapped={false} />
      </mesh>
    </group>
  )
}

/**
 * The product page's 3D view: its photo on a floating card that leans with the pointer, or, until it has a photo,
 * a 3D model that suits its category (a yarn cone, a roll of cloth, a folded stack or a carton).
 */
export default function ProductStage({ imageUrl, model, className }: { imageUrl?: string; model: ProductModel; className?: string }) {
  return (
    <SceneCanvas className={className} camera={{ position: [0, 0.35, 5], fov: 34 }}>
      <StudioLights />
      <PointerRig strength={imageUrl ? 0.55 : 0.35}>
        <Floating amplitude={0.07} spin={imageUrl ? 0 : 0.45} speed={0.9}>
          {imageUrl ? (
            <Suspense fallback={<ProductModelMesh model={model} />}>
              <PhotoCard url={imageUrl} />
            </Suspense>
          ) : (
            <ProductModelMesh model={model} />
          )}
        </Floating>
      </PointerRig>
      <SoftShadow position={[0, -1.45, 0]} radius={1.3} opacity={0.16} />
    </SceneCanvas>
  )
}
