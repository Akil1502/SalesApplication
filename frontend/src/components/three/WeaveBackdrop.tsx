import { Canvas, useFrame, useThree } from '@react-three/fiber'
import { useEffect, useMemo } from 'react'
import { Color, ShaderMaterial } from 'three'

const vertexShader = /* glsl */ `
  varying vec2 vUv;
  void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
  }
`

// A plain weave seen up close: warp and weft threads passing over and under each other, the cloth billowing
// slowly, a band of sheen drifting across, and a few bright threads running through it.
const fragmentShader = /* glsl */ `
  uniform float uTime;
  uniform vec2 uResolution;
  uniform vec3 uWarp;
  uniform vec3 uWeft;
  uniform float uStrength;
  varying vec2 vUv;

  float thread(float x) { return smoothstep(0.46, 0.2, abs(x - 0.5)); }

  void main() {
    vec2 px = vUv * uResolution;
    vec2 p = px / 26.0;
    p.x += sin(p.y * 0.17 + uTime * 0.22) * 0.9;
    p.y += cos(p.x * 0.13 + uTime * 0.18) * 0.9;
    vec2 id = floor(p);
    vec2 f = fract(p);
    bool warpOnTop = mod(id.x + id.y, 2.0) > 0.5;
    float warp = thread(f.x) * (0.55 + 0.45 * sin(f.y * 3.14159));
    float weft = thread(f.y) * (0.55 + 0.45 * sin(f.x * 3.14159));
    float amount = warpOnTop ? warp : weft;
    vec3 colour = warpOnTop ? uWarp : uWeft;

    float sheen = 0.5 + 0.5 * sin((px.x + px.y) * 0.0035 - uTime * 0.35);

    float flow = 0.0;
    for (int i = 0; i < 3; i++) {
      float fi = float(i);
      float y = 0.2 + 0.28 * fi + 0.05 * sin(vUv.x * 5.0 + uTime * (0.25 + 0.08 * fi) + fi * 2.1);
      float line = smoothstep(0.0035, 0.0, abs(vUv.y - y));
      flow += line * (0.35 + 0.65 * smoothstep(0.6, 1.0, sin(vUv.x * 9.0 - uTime * 1.4 + fi * 1.7)));
    }

    vec2 c = (vUv - vec2(0.5, 0.95)) * vec2(1.0, 1.7);
    float fade = smoothstep(1.25, 0.15, length(c));
    float alpha = (amount * (0.55 + 0.45 * sheen) + flow * 1.6) * uStrength * fade;
    gl_FragColor = vec4(mix(colour, uWarp + 0.25, flow), alpha);
  }
`

function Weave({ dark }: { dark: boolean }) {
  const size = useThree((state) => state.size)
  const invalidate = useThree((state) => state.invalidate)
  const material = useMemo(
    () =>
      new ShaderMaterial({
        vertexShader,
        fragmentShader,
        transparent: true,
        depthWrite: false,
        uniforms: {
          uTime: { value: 0 },
          uResolution: { value: [1, 1] },
          uWarp: { value: new Color() },
          uWeft: { value: new Color() },
          uStrength: { value: 0.1 },
        },
      }),
    [],
  )
  useEffect(() => () => material.dispose(), [material])

  useEffect(() => {
    material.uniforms.uWarp!.value.set(dark ? '#7f86f5' : '#4a52d4')
    material.uniforms.uWeft!.value.set(dark ? '#4fc3d4' : '#2a9fb0')
    material.uniforms.uStrength!.value = dark ? 0.16 : 0.09
    invalidate()
  }, [dark, material, invalidate])

  useEffect(() => {
    material.uniforms.uResolution!.value = [size.width, size.height]
    invalidate()
  }, [size, material, invalidate])

  // About 25 frames a second is plenty for something this slow, and spares the graphics card.
  useEffect(() => {
    const timer = setInterval(() => invalidate(), 40)
    return () => clearInterval(timer)
  }, [invalidate])

  useFrame(({ clock }) => {
    material.uniforms.uTime!.value = clock.elapsedTime
  })

  return (
    <mesh material={material}>
      <planeGeometry args={[2, 2]} />
    </mesh>
  )
}

/** The page backdrop at full motion: a softly billowing woven cloth behind everything. */
export default function WeaveBackdrop({ dark }: { dark: boolean }) {
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 -z-10 print:hidden">
      <Canvas dpr={1} frameloop="demand" gl={{ alpha: true, antialias: false, powerPreference: 'low-power' }}>
        <Weave dark={dark} />
      </Canvas>
    </div>
  )
}
