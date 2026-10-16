import type { BillMatchStatus, PurchaseOrderStatus, SupplierBillStatus } from './api'

export const purchaseOrderStatusLabels: Record<PurchaseOrderStatus, string> = {
  Draft: 'Draft',
  PendingApproval: 'Waiting for approval',
  Ordered: 'Ordered',
  PartlyReceived: 'Part received',
  Received: 'Received',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
}

export const billStatusLabels: Record<SupplierBillStatus, string> = {
  Unpaid: 'Unpaid',
  PartiallyPaid: 'Part paid',
  Paid: 'Paid',
  Cancelled: 'Cancelled',
}

export const matchStatusLabels: Record<BillMatchStatus, string> = {
  Matched: 'Matches order',
  NeedsApproval: 'Needs approval',
  Approved: 'Differences approved',
}
