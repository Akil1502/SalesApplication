import { msg, t } from '@/lib/i18n'
import type { AuditAction, AuditEntry } from './api'

/** What each action is called in the log; translate with t() where shown. */
export const actionLabels: Record<AuditAction, string> = {
  Created: msg('Created'),
  Updated: msg('Changed'),
  Deleted: msg('Removed'),
  SignedIn: msg('Signed in'),
  SignInFailed: msg('Failed sign-in'),
  LockedOut: msg('Locked out'),
  PasswordReset: msg('Password reset'),
  PermissionsChanged: msg('Access changed'),
  Exported: msg('Downloaded'),
}

/** Actions worth a second look, shown in a warning colour. */
export const isWarning = (action: AuditAction) => action === 'SignInFailed' || action === 'LockedOut'

/** The kinds of record the trail covers, as the server names them, for the filter. Shown with t(). */
export const recordTypes = [
  msg('Customer'),
  msg('Product'),
  msg('Rate'),
  msg('Category'),
  msg('HSN code'),
  msg('Order'),
  msg('Invoice'),
  msg('Payment'),
  msg('Goods receipt'),
  msg('Stock adjustment'),
  msg('Supplier'),
  msg('Purchase order'),
  msg('Supplier bill'),
  msg('Supplier payment'),
  msg('Credit note'),
  msg('Credit used'),
  msg('Credit refund'),
  msg('Supplier return'),
  msg('Supplier credit used'),
  msg('Supplier refund'),
  msg('User'),
  msg('Role'),
  msg('Company settings'),
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
    case 'Supplier':
      return id ? `/suppliers/${id}` : null
    case 'Purchase order':
      return id ? `/purchase-orders/${id}` : null
    case 'Supplier bill':
      return id ? `/supplier-bills/${id}` : null
    case 'Supplier payment':
      return '/supplier-payments'
    case 'Credit note':
      return id ? `/credit-notes/${id}` : null
    case 'Credit used':
    case 'Credit refund':
      return '/credit-notes'
    case 'Supplier return':
      return id ? `/supplier-returns/${id}` : null
    case 'Supplier credit used':
    case 'Supplier refund':
      return '/supplier-returns'
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
  return more > 0 ? t('{changes}; and {count} more', { changes: shown.join('; '), count: more }) : shown.join('; ')
}
