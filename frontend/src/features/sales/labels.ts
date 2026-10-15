import type { InvoiceStatus, OrderStatus, PaymentMode } from './api'

export const orderStatusLabels: Record<OrderStatus, string> = {
  Draft: 'Draft',
  Quoted: 'Quoted',
  Confirmed: 'Confirmed',
  Invoiced: 'Invoiced',
  Cancelled: 'Cancelled',
}

export const invoiceStatusLabels: Record<InvoiceStatus, string> = {
  Unpaid: 'Unpaid',
  PartiallyPaid: 'Part paid',
  Paid: 'Paid',
  Cancelled: 'Cancelled',
}

export const paymentModes: { value: PaymentMode; label: string }[] = [
  { value: 'Upi', label: 'UPI' },
  { value: 'Neft', label: 'NEFT' },
  { value: 'Rtgs', label: 'RTGS' },
  { value: 'Cheque', label: 'Cheque' },
  { value: 'Cash', label: 'Cash' },
  { value: 'Card', label: 'Card' },
  { value: 'Other', label: 'Other' },
]

export const paymentModeLabel = (mode: PaymentMode) => paymentModes.find((m) => m.value === mode)?.label ?? mode
