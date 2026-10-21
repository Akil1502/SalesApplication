import type { DocumentLanguage } from '@/features/sales/print/document-language'
import { formatBusinessDate, formatINR, todayInIndia } from '@/lib/format'
import { translator, type Language } from '@/lib/i18n'
import type { InvoiceListItem } from './api'

const DAY = 24 * 60 * 60 * 1000

type ReminderInvoice = Pick<InvoiceListItem, 'number' | 'customerName' | 'dueDate' | 'balance' | 'status'> & { documentLanguage?: DocumentLanguage }

/** One reminder in one language: the customer's language decides, whatever the app is showing. */
function reminder(language: Language, customer: string, list: ReminderInvoice[], signedBy: string, today: string): string {
  const { t, tn } = translator(language)
  const lines = list.map((invoice) => {
    const late = Math.round((Date.parse(today) - Date.parse(invoice.dueDate)) / DAY)
    const values = { number: invoice.number, amount: formatINR(invoice.balance), due: formatBusinessDate(invoice.dueDate, language) }
    return late > 0
      ? tn(late, '• {number}: {amount} (due {due}, {count} day overdue)', '• {number}: {amount} (due {due}, {count} days overdue)', values)
      : t('• {number}: {amount} (due {due})', values)
  })
  const total = list.reduce((sum, invoice) => sum + invoice.balance, 0)
  return [
    t('Dear {customer},', { customer }),
    '',
    list.length === 1 ? t('A gentle reminder that this invoice is due:') : t('A gentle reminder that these invoices are due:'),
    ...lines,
    ...(list.length > 1 ? [t('Total due: {amount}', { amount: formatINR(total) })] : []),
    '',
    t('Kindly arrange the payment at the earliest. If you have already paid, please ignore this message, and thank you.'),
    '',
    t('Regards,'),
    signedBy,
  ].join('\n')
}

/**
 * A payment reminder per customer for the invoices that still have something due, in plain words with the real
 * figures, in the customer's own language (English, Tamil, or the English followed by the Tamil). The person sends
 * them; the app never does. Tamil needs the Tamil texts loaded (see useTamilTexts).
 */
export function reminderMessages(invoices: ReminderInvoice[], signedBy: string, today = todayInIndia()): { customer: string; text: string }[] {
  const byCustomer = new Map<string, ReminderInvoice[]>()
  for (const invoice of invoices) {
    if (invoice.balance <= 0 || invoice.status === 'Cancelled') continue
    byCustomer.set(invoice.customerName, [...(byCustomer.get(invoice.customerName) ?? []), invoice])
  }

  return Array.from(byCustomer, ([customer, list]) => {
    const language = list[0]?.documentLanguage ?? 'English'
    const english = language === 'Tamil' ? '' : reminder('en', customer, list, signedBy, today)
    const tamil = language === 'English' ? '' : reminder('ta', customer, list, signedBy, today)
    return { customer, text: [english, tamil].filter(Boolean).join('\n\n— — —\n\n') }
  })
}
