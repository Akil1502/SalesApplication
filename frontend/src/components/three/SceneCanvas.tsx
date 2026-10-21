import { Canvas, type CanvasProps } from '@react-three/fiber'
import { useEffect, useRef, useState } from 'react'
import { ACESFilmicToneMapping, SRGBColorSpace } from 'three'
import { cn } from '@/lib/utils'

/**
 * A 3D canvas that only animates while it's on screen (scrolled away, it stops drawing), with sensible defaults:
 * a transparent background so the page shows through, filmic colours, and a pixel ratio capped for laptops.
 */
export function SceneCanvas({ className, children, ...props }: CanvasProps & { className?: string }) {
  const host = useRef<HTMLDivElement>(null)
  const [onScreen, setOnScreen] = useState(true)

  useEffect(() => {
    const element = host.current
    if (!element || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver(([entry]) => setOnScreen(entry?.isIntersecting ?? true), { rootMargin: '80px' })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return (
    <div ref={host} className={cn('relative', className)}>
      <Canvas
        dpr={[1, 1.75]}
        frameloop={onScreen ? 'always' : 'never'}
        gl={{ antialias: true, alpha: true, powerPreference: 'high-performance' }}
        onCreated={({ gl }) => {
          gl.toneMapping = ACESFilmicToneMapping
          gl.outputColorSpace = SRGBColorSpace
        }}
        {...props}
      >
        {children}
      </Canvas>
    </div>
  )
}
