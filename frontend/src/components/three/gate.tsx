import { Component, Suspense, type ErrorInfo, type ReactNode } from 'react'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { webglAvailable } from './webgl'

class SceneErrorBoundary extends Component<{ fallback: ReactNode; children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.warn('A 3D scene could not be shown; showing the still version instead.', error, info.componentStack)
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children
  }
}

/**
 * Shows a 3D scene only at full motion and when the browser can draw 3D; otherwise, while the 3D code downloads,
 * or if the scene fails, it shows the still fallback. The 3D library is only downloaded when a scene is shown.
 */
export function Scene3DGate({ fallback, children }: { fallback: ReactNode; children: ReactNode }) {
  const level = useMotionLevel()
  if (level !== 'full' || !webglAvailable()) return fallback
  return (
    <SceneErrorBoundary fallback={fallback}>
      <Suspense fallback={fallback}>{children}</Suspense>
    </SceneErrorBoundary>
  )
}
