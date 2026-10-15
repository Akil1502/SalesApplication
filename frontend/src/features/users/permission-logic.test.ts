import { describe, expect, it } from 'vitest'
import { changesFrom, effective, toggle, withNeeds, type Needs } from './permission-logic'

const needs: Needs = {
  'orders.edit': ['orders.view'],
  'smart-entry.use': ['orders.edit'],
  'payments.edit': ['payments.view', 'invoices.view'],
}

const sorted = (set: Set<string>) => [...set].sort()

describe('permission rules', () => {
  it('grants what a permission needs, all the way down', () => {
    expect(sorted(withNeeds(['smart-entry.use'], needs))).toEqual(['orders.edit', 'orders.view', 'smart-entry.use'])
    expect(sorted(withNeeds(['payments.edit'], needs))).toEqual(['invoices.view', 'payments.edit', 'payments.view'])
  })

  it('unticking view also unticks what depends on it', () => {
    const all = withNeeds(['smart-entry.use', 'reports.view'], needs)
    expect(sorted(toggle(all, 'orders.view', false, needs))).toEqual(['reports.view'])
    expect(sorted(toggle(new Set(['reports.view']), 'orders.edit', true, needs))).toEqual(['orders.edit', 'orders.view', 'reports.view'])
  })

  it("works out a user's permissions and stores only the differences from their role", () => {
    const role = new Set(['orders.view', 'orders.edit', 'reports.view'])
    const user = effective(role, ['ai-usage.view'], ['orders.view'], needs)
    expect(sorted(user)).toEqual(['ai-usage.view', 'reports.view'])

    expect(changesFrom(role, user)).toEqual({ addedPermissions: ['ai-usage.view'], removedPermissions: ['orders.edit', 'orders.view'] })
    expect(changesFrom(role, role)).toEqual({ addedPermissions: [], removedPermissions: [] })
  })
})
