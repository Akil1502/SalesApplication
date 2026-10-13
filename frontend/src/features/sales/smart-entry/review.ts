import type { PickedProduct } from '@/components/ProductPicker'
import { formatBusinessDate } from '@/lib/format'
import type { ExtractedLine, ExtractionConfidence, OrderExtraction, ProductMatchSource, SmartOrderConfirmRequest } from './api'

/** One line on the review screen: what the order said, what was suggested, and what the user has settled on. */
export interface ReviewLine {
  key: string
  sourceText: string
  /** The product the AI (or a learned alias) suggested; a different final choice is a correction worth learning. */
  suggestedProductId: number | null
  product: PickedProduct | null
  quantity: string
  statedUnit: string | null
  statedPrice: number | null
  /** Stated price against list price, as a fraction; only meaningful while the suggested product is kept. */
  priceVariance: number | null
  confidence: ExtractionConfidence
  matchedBy: ProductMatchSource
  note: string | null
}

let nextKey = 0
const newKey = () => `line-${++nextKey}`

export function toReviewLine(line: ExtractedLine): ReviewLine {
  return {
    key: newKey(),
    sourceText: line.sourceText,
    suggestedProductId: line.product?.id ?? null,
    product: line.product,
    quantity: line.quantity === null ? '' : String(line.quantity),
    statedUnit: line.statedUnit,
    statedPrice: line.statedPrice,
    priceVariance: line.priceVariance,
    confidence: line.confidence,
    matchedBy: line.matchedBy,
    note: line.note,
  }
}

/** A line the user adds by hand, optionally for a fragment the AI couldn't read. */
export function blankLine(sourceText = ''): ReviewLine {
  return {
    key: newKey(),
    sourceText,
    suggestedProductId: null,
    product: null,
    quantity: '',
    statedUnit: null,
    statedPrice: null,
    priceVariance: null,
    confidence: 'Low',
    matchedBy: 'None',
    note: null,
  }
}

/** Quantities are positive with at most three decimals (kg to the gram), as the server requires. */
export function parseQuantity(value: string): number | null {
  const trimmed = value.trim()
  if (!/^\d+(\.\d{1,3})?$/.test(trimmed)) return null
  const quantity = Number(trimmed)
  return quantity > 0 ? quantity : null
}

export function lineProblem(line: ReviewLine): string | null {
  if (!line.product) return 'Choose a product'
  if (parseQuantity(line.quantity) === null) return 'Enter a quantity'
  return null
}

/** The stated price is off the list price by 2% or more, for the product that was suggested. */
export function priceDiffers(line: ReviewLine): boolean {
  return line.priceVariance !== null && line.product?.id === line.suggestedProductId && Math.abs(line.priceVariance) >= 0.02
}

/** Needs a second look: unmatched, unsure, a unit that had to be converted, or a price that differs from the list. */
export function needsAttention(line: ReviewLine): boolean {
  return line.confidence !== 'High' || line.matchedBy === 'None' || line.statedUnit !== null || priceDiffers(line)
}

/** The order's own notes plus the delivery date it asked for, which has no field of its own on an order. */
export function initialNotes(extraction: OrderExtraction): string {
  const parts = [extraction.notes?.trim()]
  if (extraction.requestedDeliveryDate) parts.push(`Requested delivery: ${formatBusinessDate(extraction.requestedDeliveryDate)}`)
  return parts.filter(Boolean).join('\n')
}

export function buildConfirmRequest(input: {
  customerId: number
  customerReference: string
  notes: string
  lines: ReviewLine[]
  source: OrderExtraction['source']
}): SmartOrderConfirmRequest {
  return {
    order: {
      customerId: input.customerId,
      customerReference: input.customerReference.trim() || null,
      notes: input.notes.trim() || null,
      source: input.source,
      // No unit price: the server prices every line from the price list.
      lines: input.lines.map((line) => ({ productId: line.product!.id, quantity: parseQuantity(line.quantity)!, discountPercent: 0 })),
    },
    source: input.source,
    corrections: input.lines
      .filter((line) => line.sourceText.trim() && line.product && line.product.id !== line.suggestedProductId)
      .map((line) => ({ sourceText: line.sourceText.trim(), productId: line.product!.id })),
  }
}
