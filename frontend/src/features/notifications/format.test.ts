import { describe, expect, it } from 'vitest'
import type { Notification } from './api'
import { arrivedSince, groupByDay, whenText } from './format'

const now = new Date('2026-10-17T10:00:00Z') // 3:30 pm in India

const item = (id: number, createdAtUtc: string): Notification => ({
  id,
  kind: 'PaymentReceived',
  title: `Item ${id}`,
  body: null,
  link: '/invoices/1',
  createdAtUtc,
  read: false,
  important: true,
})

describe('notification times', () => {
  it('reads as people say it, in India time', () => {
    expect(whenText('2026-10-17T09:59:40Z', now)).toBe('Just now')
    expect(whenText('2026-10-17T09:48:00Z', now)).toBe('12 min ago')
    expect(whenText('2026-10-17T04:35:00Z', now)).toBe('10:05 am')
    expect(whenText('2026-10-16T10:35:00Z', now)).toBe('Yesterday, 4:05 pm')
    expect(whenText('2026-10-12T06:00:00Z', now)).toBe('12 Oct')
  })

  it('groups by India day, so late evening UTC can already be tomorrow', () => {
    const groups = groupByDay([item(4, '2026-10-17T09:00:00Z'), item(3, '2026-10-16T19:00:00Z'), item(2, '2026-10-16T10:00:00Z'), item(1, '2026-10-10T10:00:00Z')], now)
    expect(groups.map((g) => [g.label, g.items.map((i) => i.id)])).toEqual([
      ['Today', [4, 3]],
      ['Yesterday', [2]],
      ['Earlier', [1]],
    ])
  })

  it('finds what arrived since the last look, oldest first', () => {
    expect(arrivedSince([item(9, ''), item(8, ''), item(5, '')], 6).map((i) => i.id)).toEqual([8, 9])
  })
})
