import { describe, expect, it } from 'vitest'
import { reminderMessages } from './reminders'

const invoice = (number: string, customerName: string, balance: number, dueDate: string, status: 'Unpaid' | 'PartiallyPaid' | 'Paid' | 'Cancelled' = 'Unpaid') => ({
  number,
  customerName,
  balance,
  dueDate,
  status,
})

describe('payment reminders', () => {
  it('writes one message per customer, with days overdue and a total', () => {
    const messages = reminderMessages(
      [
        invoice('INV/26-27/00801', 'Pandian Knits', 50000, '2026-10-10'),
        invoice('INV/26-27/00806', 'Pandian Knits', 25000.5, '2026-10-20'),
        invoice('INV/26-27/00807', 'Kongu Distributors', 1000, '2026-10-16'),
      ],
      'Asha Raman',
      '2026-10-17',
    )

    expect(messages.map((m) => m.customer)).toEqual(['Pandian Knits', 'Kongu Distributors'])
    const pandian = messages[0]!.text
    expect(pandian).toContain('these invoices are due')
    expect(pandian).toContain('INV/26-27/00801: ₹50,000.00 (due 10 Oct 2026, 7 days overdue)')
    expect(pandian).toContain('INV/26-27/00806: ₹25,000.50 (due 20 Oct 2026)')
    expect(pandian).toContain('Total due: ₹75,000.50')
    expect(pandian.endsWith('Regards,\nAsha Raman')).toBe(true)
    expect(messages[1]!.text).toContain('1 day overdue')
    expect(messages[1]!.text).not.toContain('Total due')
  })

  it('leaves out invoices with nothing due', () => {
    expect(reminderMessages([invoice('A', 'X', 0, '2026-10-01', 'Paid'), invoice('B', 'Y', 500, '2026-10-01', 'Cancelled')], 'Asha')).toEqual([])
  })
})
