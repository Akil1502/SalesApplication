import { msg, t } from '@/lib/i18n'
import type { InvoiceStatus, OrderStatus, PaymentMode } from './api'

/** Translate with t() where they're shown (the badges in ./badges do). */
export const orderStatusLabels: Record<OrderStatus, string> = {
  Draft: msg('Draft'),
  Quoted: msg('Quoted'),
  Confirmed: msg('Confirmed'),
  Invoiced: msg('Invoiced'),
  Cancelled: msg('Cancelled'),
}

export const invoiceStatusLabels: Record<InvoiceStatus, string> = {
  Unpaid: msg('Unpaid'),
  PartiallyPaid: msg('Part paid'),
  Paid: msg('Paid'),
  Cancelled: msg('Cancelled'),
}

const modeLabels: Record<PaymentMode, string> = {
  Upi: 'UPI',
  Neft: 'NEFT',
  Rtgs: 'RTGS',
  Cheque: msg('Cheque'),
  Cash: msg('Cash'),
  Card: msg('Card'),
  Other: msg('Other'),
}

/** Each label is read when it's shown, so it's in the language on screen wherever the list is used. */
export const paymentModes: { value: PaymentMode; readonly label: string }[] = (Object.keys(modeLabels) as PaymentMode[]).map((value) => ({
  value,
  get label() {
    return t(modeLabels[value])
  },
}))

export const paymentModeLabel = (mode: PaymentMode) => (mode in modeLabels ? t(modeLabels[mode]) : mode)
