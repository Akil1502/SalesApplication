import { msg, t } from '@/lib/i18n'
import type { CreditNoteKind, ReturnDisposition, ReturnDocumentStatus, ReturnReason } from './api'

export const creditNoteKindLabels: Record<CreditNoteKind, string> = {
  GoodsReturn: msg('Goods returned'),
  PriceCorrection: msg('Price correction'),
}

export const returnReasonLabels: Record<ReturnReason, string> = {
  Damaged: msg('Damaged'),
  WrongItem: msg('Wrong item'),
  QualityIssue: msg('Quality issue'),
  ExcessSupplied: msg('Excess supplied'),
  RateDifference: msg('Rate difference'),
  ShortSupply: msg('Short supply'),
  Discount: msg('Discount after the sale'),
  Other: msg('Other'),
}

/** Reasons that fit goods coming back, and ones that fit a value-only correction. */
export const goodsReasons: ReturnReason[] = ['Damaged', 'WrongItem', 'QualityIssue', 'ExcessSupplied', 'Other']
export const priceReasons: ReturnReason[] = ['RateDifference', 'ShortSupply', 'Discount', 'Other']

export const dispositionLabels: Record<ReturnDisposition, string> = {
  Restock: msg('Back into stock'),
  Damaged: msg('Damaged: write off'),
}

export const returnStatusLabels: Record<ReturnDocumentStatus, string> = {
  Issued: msg('Issued'),
  Cancelled: msg('Cancelled'),
}

/** Options for a select; the labels are translated here, so call it while rendering. */
export const reasonOptions = (reasons: ReturnReason[]) => reasons.map((r) => ({ value: r, label: t(returnReasonLabels[r]) }))
