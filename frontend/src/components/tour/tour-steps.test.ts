import { describe, expect, it } from 'vitest'
import { Permissions } from '@/features/auth/permissions'
import { tourSteps, type TourContext } from './tour-steps'

const base: TourContext = { appName: 'Demo Textiles', firstName: 'Meena', layout: 'rail', phone: false, can: () => true, aiEnabled: true }
const ids = (context: Partial<TourContext>) => tourSteps({ ...base, ...context }).map((s) => s.id)

describe('guided tour', () => {
  it('greets the person by name in the company’s name', () => {
    expect(tourSteps(base)[0]!.title).toBe('Welcome to Demo Textiles, Meena')
  })

  it('describes the menu in the layout they chose', () => {
    const menu = (layout: TourContext['layout']) => tourSteps({ ...base, layout }).find((s) => s.id === 'menu')!.body
    expect(menu('rail')).toMatch(/on the left/)
    expect(menu('top')).toMatch(/along the top/)
    expect(menu('dock')).toMatch(/dock at the bottom/)
  })

  it('on a phone points at the bottom bar and ＋, and leaves out keyboard tips', () => {
    const steps = tourSteps({ ...base, phone: true })
    expect(steps.map((s) => s.id)).toContain('new')
    expect(steps.map((s) => s.id)).not.toContain('records')
    expect(steps.find((s) => s.id === 'menu')!.body).toMatch(/at the bottom/)
    expect(steps.at(-1)!.body).not.toMatch(/\?/)
  })

  it('shows each person only what their role can do', () => {
    const rep = (...anyOf: string[]) => anyOf.some((p) => [Permissions.DashboardView, Permissions.OrdersEdit, Permissions.SmartEntryUse].includes(p as never))
    expect(ids({ can: rep })).toContain('smart-entry')
    expect(ids({ can: rep })).not.toContain('approvals')
    expect(ids({ aiEnabled: false })).not.toContain('smart-entry')
    expect(ids({ can: () => false })).toEqual(['welcome', 'menu', 'search', 'notifications', 'records', 'account', 'done'])
  })
})
