import { describe, expect, it } from 'vitest'
import { modelForCategory } from '@/components/three/product-models'
import { initials } from '@/lib/product-tile'
import { centreSquare } from './images'

describe('product photos', () => {
  it('crops the thumbnail from the centre of the picture', () => {
    expect(centreSquare(1600, 1200)).toEqual({ x: 200, y: 0, side: 1200 })
    expect(centreSquare(900, 1600)).toEqual({ x: 0, y: 350, side: 900 })
    expect(centreSquare(500, 500)).toEqual({ x: 0, y: 0, side: 500 })
  })

  it('shows initials until there is a photo', () => {
    expect(initials('Cotton yarn 20s carded')).toBe('CY')
    expect(initials('Polyester-viscose yarn 30s (65/35)')).toBe('PV')
    expect(initials(undefined)).toBe('')
  })

  it('picks a 3D model to suit the category', () => {
    expect(modelForCategory('Cotton yarn')).toBe('yarn')
    expect(modelForCategory('Fabrics')).toBe('fabric')
    expect(modelForCategory('Packing material')).toBe('carton')
    expect(modelForCategory('Home textiles')).toBe('stack')
    expect(modelForCategory(undefined)).toBe('stack')
  })
})
