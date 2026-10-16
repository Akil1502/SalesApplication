import { describe, expect, it } from 'vitest'
import type { ExtractedLine, OrderExtraction } from './api'
import { fitWithin } from './image'
import { blankLine, buildConfirmRequest, initialNotes, lineProblem, needsAttention, parseQuantity, toReviewLine } from './review'

const extracted = (overrides: Partial<ExtractedLine> = {}): ExtractedLine => ({
  sourceText: '30s cc 500kg',
  product: { id: 7, sku: 'CY-30C', name: 'Cotton yarn 30s combed', unit: 'Kg', price: 300, quantityOnHand: 1200, gstRate: 5 },
  quantity: 500,
  statedUnit: null,
  statedPrice: null,
  priceVariance: null,
  confidence: 'High',
  matchedBy: 'Ai',
  note: null,
  ...overrides,
})

describe('review lines', () => {
  it('start from the suggestion', () => {
    const line = toReviewLine(extracted())
    expect(line.product).toMatchObject({ id: 7, sku: 'CY-30C', quantityOnHand: 1200 })
    expect(line.quantity).toBe('500')
    expect(lineProblem(line)).toBeNull()
    expect(needsAttention(line)).toBe(false)
  })

  it('treat fields the API left out as empty', () => {
    // The server omits nulls from its JSON, so a clear line arrives without statedUnit, statedPrice or note at all.
    const { statedUnit, statedPrice, priceVariance, note, ...sent } = extracted()
    void [statedUnit, statedPrice, priceVariance, note]
    const line = toReviewLine(sent as ExtractedLine)
    expect(line.statedUnit).toBeNull()
    expect(line.statedPrice).toBeNull()
    expect(needsAttention(line)).toBe(false)
  })

  it('flag what needs a second look', () => {
    expect(needsAttention(toReviewLine(extracted({ confidence: 'Medium' })))).toBe(true)
    expect(needsAttention(toReviewLine(extracted({ statedUnit: 'bags' })))).toBe(true)
    expect(needsAttention(toReviewLine(extracted({ statedPrice: 280, priceVariance: -0.0667 })))).toBe(true)
    expect(needsAttention(toReviewLine(extracted({ statedPrice: 301, priceVariance: 0.0033 })))).toBe(false)
  })

  it('need a product and a sensible quantity', () => {
    expect(lineProblem(toReviewLine(extracted({ product: null })))).toBe('Choose a product')
    expect(lineProblem({ ...toReviewLine(extracted()), quantity: '0' })).toBe('Enter a quantity')
    expect(parseQuantity('12.5')).toBe(12.5)
    expect(parseQuantity('1.2345')).toBeNull()
    expect(parseQuantity('-3')).toBeNull()
    expect(parseQuantity('abc')).toBeNull()
  })
})

describe('confirming', () => {
  it('prices from the list and learns only from corrections', () => {
    const kept = toReviewLine(extracted())
    const changed = { ...toReviewLine(extracted({ sourceText: 'kora 30s' })), product: { ...kept.product!, id: 9, sku: 'CY-30K' } }
    const filledIn = { ...blankLine('red thread'), product: { ...kept.product!, id: 11 }, quantity: '25' }
    const handAdded = { ...blankLine(), product: { ...kept.product!, id: 12 }, quantity: '1' }

    const request = buildConfirmRequest({
      customerId: 3,
      customerReference: ' PO-9 ',
      notes: '',
      lines: [kept, changed, filledIn, handAdded],
      source: 'AiText',
    })

    expect(request.order.customerReference).toBe('PO-9')
    expect(request.order.notes).toBeNull()
    expect(request.order.lines).toEqual([
      { productId: 7, quantity: 500, discountPercent: 0 },
      { productId: 9, quantity: 500, discountPercent: 0 },
      { productId: 11, quantity: 25, discountPercent: 0 },
      { productId: 12, quantity: 1, discountPercent: 0 },
    ])
    expect(request.order.lines.every((l) => !('unitPrice' in l))).toBe(true)
    expect(request.corrections).toEqual([
      { sourceText: 'kora 30s', productId: 9 },
      { sourceText: 'red thread', productId: 11 },
    ])
  })

  it('carries the requested delivery date into the notes', () => {
    const extraction = { notes: 'Send by KPN', requestedDeliveryDate: '2026-10-20' } as OrderExtraction
    expect(initialNotes(extraction)).toBe('Send by KPN\nRequested delivery: 20 Oct 2026')
  })
})

describe('fitWithin', () => {
  it('shrinks large photos to the long-edge limit and leaves small ones alone', () => {
    expect(fitWithin(4032, 3024)).toEqual({ width: 1568, height: 1176 })
    expect(fitWithin(1080, 1920)).toEqual({ width: 882, height: 1568 })
    expect(fitWithin(800, 600)).toEqual({ width: 800, height: 600 })
  })
})
