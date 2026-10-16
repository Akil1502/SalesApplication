import { describe, expect, it } from 'vitest'
import { changeSummary, recordLink } from './audit-format'

describe('audit log helpers', () => {
  it('links records to their pages', () => {
    expect(recordLink({ entityType: 'Customer', entityId: '42' })).toBe('/customers/42')
    expect(recordLink({ entityType: 'Invoice', entityId: '7' })).toBe('/invoices/7')
    expect(recordLink({ entityType: 'Role', entityId: 'x' })).toBe('/admin/users?tab=roles')
    expect(recordLink({ entityType: null, entityId: null })).toBeNull()
  })

  it('sums up changes, or falls back to the summary', () => {
    const changes = [
      { field: 'Credit limit', old: '₹5,00,000', new: '₹7,50,000' },
      { field: 'Payment terms (days)', old: '30', new: '45' },
      { field: 'Sales rep', old: null, new: 'Divya Rajan' },
    ]
    expect(changeSummary({ changes, summary: null })).toBe('Credit limit: ₹5,00,000 → ₹7,50,000; Payment terms (days): 30 → 45; and 1 more')
    expect(changeSummary({ changes: changes.slice(2), summary: null })).toBe('Sales rep: — → Divya Rajan')
    expect(changeSummary({ changes: [], summary: 'Signed in.' })).toBe('Signed in.')
  })
})
