import type { CreditNoteKind, ReturnDisposition, ReturnDocumentStatus, ReturnReason } from './api'

export const creditNoteKindLabels: Record<CreditNoteKind, string> = {
  GoodsReturn: 'Goods returned',
  PriceCorrection: 'Price correction',
}

export const returnReasonLabels: Record<ReturnReason, string> = {
  Damaged: 'Damaged',
  WrongItem: 'Wrong item',
  QualityIssue: 'Quality issue',
  ExcessSupplied: 'Excess supplied',
  RateDifference: 'Rate difference',
  ShortSupply: 'Short supply',
  Discount: 'Discount after the sale',
  Other: 'Other',
}

/** Reasons that fit goods coming back, and ones that fit a value-only correction. */
export const goodsReasons: ReturnReason[] = ['Damaged', 'WrongItem', 'QualityIssue', 'ExcessSupplied', 'Other']
export const priceReasons: ReturnReason[] = ['RateDifference', 'ShortSupply', 'Discount', 'Other']

export const dispositionLabels: Record<ReturnDisposition, string> = {
  Restock: 'Back into stock',
  Damaged: 'Damaged: write off',
}

export const returnStatusLabels: Record<ReturnDocumentStatus, string> = {
  Issued: 'Issued',
  Cancelled: 'Cancelled',
}

export const reasonOptions = (reasons: ReturnReason[]) => reasons.map((r) => ({ value: r, label: returnReasonLabels[r] }))
