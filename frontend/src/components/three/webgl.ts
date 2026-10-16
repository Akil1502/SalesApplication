let webgl: boolean | undefined

/** Whether this browser can draw 3D (WebGL). Checked once. */
export function webglAvailable(): boolean {
  if (webgl === undefined) {
    try {
      const canvas = document.createElement('canvas')
      webgl = !!(canvas.getContext('webgl2') ?? canvas.getContext('webgl'))
    } catch {
      webgl = false
    }
  }
  return webgl
}
