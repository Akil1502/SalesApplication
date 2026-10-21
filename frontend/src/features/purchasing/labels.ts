import { msg } from '@/lib/i18n'
import type { BillMatchStatus, PurchaseOrderStatus, SupplierBillStatus } from './api'

export const purchaseOrderStatusLabels: Record<PurchaseOrderStatus, string> = {
  Draft: msg('Draft'),
  PendingApproval: msg('Waiting for approval'),
  Ordered: msg('Ordered'),
  PartlyReceived: msg('Part received'),
  Received: msg('Received'),
  Closed: msg('Closed'),
  Cancelled: msg('Cancelled'),
}

export const billStatusLabels: Record<SupplierBillStatus, string> = {
  Unpaid: msg('Unpaid'),
  PartiallyPaid: msg('Part paid'),
  Paid: msg('Paid'),
  Cancelled: msg('Cancelled'),
}

export const matchStatusLabels: Record<BillMatchStatus, string> = {
  Matched: msg('Matches order'),
  NeedsApproval: msg('Needs approval'),
  Approved: msg('Differences approved'),
}
