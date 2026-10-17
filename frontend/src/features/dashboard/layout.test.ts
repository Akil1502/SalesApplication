import { describe, expect, it } from 'vitest'
import { Permissions } from '@/features/auth/permissions'
import { allowedCards, CARD_IDS, DEFAULT_LAYOUT, moveTo, normaliseLayout, TILE_IDS } from './layout'

describe('dashboard layout', () => {
  it('starts from the default when nothing usable is saved', () => {
    expect(normaliseLayout(undefined)).toEqual(DEFAULT_LAYOUT)
    expect(normaliseLayout('nonsense')).toEqual(DEFAULT_LAYOUT)
  })

  it('keeps the saved order, sizes and hidden cards, and adds anything new at its default place', () => {
    const layout = normaliseLayout({
      period: 'Quarter',
      city: false,
      tiles: [{ id: 'stock' }, { id: 'sales', hidden: true }, { id: 'retired-tile' }],
      cards: [{ id: 'chase', size: 'wide' }, { id: 'trend', size: 'normal', hidden: true }, { id: 'trend', size: 'wide' }, { id: 42 }],
    })

    expect(layout.period).toBe('Quarter')
    expect(layout.city).toBe(false)
    expect(layout.tiles.map((t) => t.id)).toEqual(['stock', 'sales', 'collected', 'receivables', 'orders'])
    expect(layout.tiles[1]).toEqual({ id: 'sales', hidden: true })
    expect(layout.cards.slice(0, 2)).toEqual([
      { id: 'chase', size: 'wide', hidden: false },
      { id: 'trend', size: 'normal', hidden: true },
    ])
    expect(layout.cards.map((c) => c.id).sort()).toEqual([...CARD_IDS].sort())
    expect(layout.cards.find((c) => c.id === 'quick-actions')?.hidden).toBe(true)
    expect(layout.tiles).toHaveLength(TILE_IDS.length)
  })

  it('ignores a period it doesn’t know', () => {
    expect(normaliseLayout({ period: 'Decade' }).period).toBe(DEFAULT_LAYOUT.period)
  })

  it('leaves out cards the person may not see', () => {
    const can = (...anyOf: string[]) => anyOf.includes(Permissions.InvoicesView)
    const ids = allowedCards(DEFAULT_LAYOUT, can).map((c) => c.id)
    expect(ids).toContain('chase')
    expect(ids).toContain('quick-actions')
    expect(ids).not.toContain('payments')
    expect(ids).not.toContain('purchases')
  })

  it('moves a card to where it was dropped', () => {
    const list = [{ id: 'a' }, { id: 'b' }, { id: 'c' }, { id: 'd' }]
    expect(moveTo(list, 'a', 'c').map((x) => x.id)).toEqual(['b', 'c', 'a', 'd'])
    expect(moveTo(list, 'd', 'a').map((x) => x.id)).toEqual(['d', 'a', 'b', 'c'])
    expect(moveTo(list, 'a', 'missing')).toBe(list)
  })
})
