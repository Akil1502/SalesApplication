import { formatBusinessDate, formatINR, todayInIndia } from '@/lib/format'
import type { InvoiceListItem } from './api'

const DAY = 24 * 60 * 60 * 1000

type ReminderInvoice = Pick<InvoiceListItem, 'number' | 'customerName' | 'dueDate' | 'balance' | 'status'>

/**
 * A payment reminder per customer for the invoices that still have something due, in plain words with the real
 * figures. The person sends them; the app never does.
 */
export function reminderMessages(invoices: ReminderInvoice[], signedBy: string, today = todayInIndia()): { customer: string; text: string }[] {
  const byCustomer = new Map<string, ReminderInvoice[]>()
  for (const invoice of invoices) {
    if (invoice.balance <= 0 || invoice.status === 'Cancelled') continue
    byCustomer.set(invoice.customerName, [...(byCustomer.get(invoice.customerName) ?? []), invoice])
  }

  return Array.from(byCustomer, ([customer, list]) => {
    const lines = list.map((invoice) => {
      const late = Math.round((Date.parse(today) - Date.parse(invoice.dueDate)) / DAY)
      const due = `due ${formatBusinessDate(invoice.dueDate)}`
      return `• ${invoice.number}: ${formatINR(invoice.balance)} (${late > 0 ? `${due}, ${late} ${late === 1 ? 'day' : 'days'} overdue` : due})`
    })
    const total = list.reduce((sum, invoice) => sum + invoice.balance, 0)
    const text = [
      `Dear ${customer},`,
      '',
      list.length === 1 ? 'A gentle reminder that this invoice is due:' : 'A gentle reminder that these invoices are due:',
      ...lines,
      ...(list.length > 1 ? [`Total due: ${formatINR(total)}`] : []),
      '',
      'Kindly arrange the payment at the earliest. If you have already paid, please ignore this message, and thank you.',
      '',
      'Regards,',
      signedBy,
    ].join('\n')
    return { customer, text }
  })
}
