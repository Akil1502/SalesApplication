import { WeaveBackdrop3D } from '@/components/three/scenes'
import { AuroraBackground } from './AuroraBackground'
import { useResolvedTheme } from './preferences-context'

/** Behind every page: drifting colour, and at full motion a slowly billowing woven cloth over it. */
export function Backdrop() {
  const theme = useResolvedTheme()
  return (
    <>
      <AuroraBackground />
      <WeaveBackdrop3D dark={theme === 'dark'} fallback={null} />
    </>
  )
}
