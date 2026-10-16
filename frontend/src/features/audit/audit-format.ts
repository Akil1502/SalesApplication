import type { AuditAction, AuditEntry } from './api'

/** What each action is called in the log. */
export const actionLabels: Record<AuditAction, string> = {
  Created: 'Created',
  Updated: 'Changed',
  Deleted: 'Removed',
  SignedIn: 'Signed in',
  SignInFailed: 'Failed sign-in',
  LockedOut: 'Locked out',
  PasswordReset: 'Password reset',
  PermissionsChanged: 'Access changed',
  Exported: 'Downloaded',
}

/** Actions worth a second look, shown in a warning colour. */
export const isWarning = (action: AuditAction) => action === 'SignInFailed' || action === 'LockedOut'

/** The kinds of record the trail covers, as the server names them, for the filter. */
export const recordTypes = [
  'Customer',
  'Product',
  'Rate',
  'Category',
  'HSN code',
  'Order',
  'Invoice',
  'Payment',
  'Goods receipt',
  'Stock adjustment',
  'User',
  'Role',
  'Company settings',
] as const

/** Where a record can be opened in the app, if anywhere. */
export function recordLink(entry: Pick<AuditEntry, 'entityType' | 'entityId'>): string | null {
  const id = entry.entityId
  switch (entry.entityType) {
    case 'Customer':
      return id ? `/customers/${id}` : null
    case 'Product':
      return id ? `/products/${id}` : null
    case 'Order':
      return id ? `/orders/${id}` : null
    case 'Invoice':
      return id ? `/invoices/${id}` : null
    case 'Goods receipt':
      return id ? `/stock/receipts/${id}` : null
    case 'Rate':
      return '/rates?tab=history'
    case 'Category':
    case 'HSN code':
      return '/catalog'
    case 'Payment':
      return '/payments'
    case 'Stock adjustment':
      return '/stock?tab=movements'
    case 'User':
      return '/admin/users'
    case 'Role':
      return '/admin/users?tab=roles'
    case 'Company settings':
      return '/admin/settings'
    default:
      return null
  }
}

/** One line for the list: the first changes, then how many more. */
export function changeSummary(entry: Pick<AuditEntry, 'changes' | 'summary'>, show = 2): string {
  if (entry.changes.length === 0) return entry.summary ?? ''
  const shown = entry.changes.slice(0, show).map((c) => `${c.field}: ${c.old ?? '—'} → ${c.new ?? '—'}`)
  const more = entry.changes.length - show
  return more > 0 ? `${shown.join('; ')}; and ${more} more` : shown.join('; ')
}
