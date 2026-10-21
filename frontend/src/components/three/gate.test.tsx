import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Scene3DGate } from './gate'

describe('Scene3DGate', () => {
  it('shows the still version unless motion is full', () => {
    // Outside the preferences provider motion counts as off, so the scene never loads.
    render(
      <Scene3DGate fallback={<p>Still picture</p>}>
        <p>3D scene</p>
      </Scene3DGate>,
    )
    expect(screen.getByText('Still picture')).toBeInTheDocument()
    expect(screen.queryByText('3D scene')).not.toBeInTheDocument()
  })
})
