import { lazy, type ComponentProps, type ReactNode } from 'react'
import { Scene3DGate } from './gate'
import type AiOrbScene from './AiOrb'
import type ProductStageScene from './ProductStage'
import type SalesCityScene from './SalesCity'
import type TextileHeroScene from './TextileHero'
import type WeaveBackdropScene from './WeaveBackdrop'

// Each scene (and the 3D library) downloads the first time it's shown, never with the rest of the app.
const TextileHero = lazy(() => import('./TextileHero'))
const SalesCity = lazy(() => import('./SalesCity'))
const AiOrb = lazy(() => import('./AiOrb'))
const ProductStage = lazy(() => import('./ProductStage'))
const WeaveBackdrop = lazy(() => import('./WeaveBackdrop'))

type WithFallback<T> = T & { fallback: ReactNode }

export function TextileHero3D({ fallback, ...props }: WithFallback<ComponentProps<typeof TextileHeroScene>>) {
  return (
    <Scene3DGate fallback={fallback}>
      <TextileHero {...props} />
    </Scene3DGate>
  )
}

export function SalesCity3D({ fallback, ...props }: WithFallback<ComponentProps<typeof SalesCityScene>>) {
  return (
    <Scene3DGate fallback={fallback}>
      <SalesCity {...props} />
    </Scene3DGate>
  )
}

export function AiOrb3D({ fallback, ...props }: WithFallback<ComponentProps<typeof AiOrbScene>>) {
  return (
    <Scene3DGate fallback={fallback}>
      <AiOrb {...props} />
    </Scene3DGate>
  )
}

export function ProductStage3D({ fallback, ...props }: WithFallback<ComponentProps<typeof ProductStageScene>>) {
  return (
    <Scene3DGate fallback={fallback}>
      <ProductStage {...props} />
    </Scene3DGate>
  )
}

export function WeaveBackdrop3D({ fallback, ...props }: WithFallback<ComponentProps<typeof WeaveBackdropScene>>) {
  return (
    <Scene3DGate fallback={fallback}>
      <WeaveBackdrop {...props} />
    </Scene3DGate>
  )
}
