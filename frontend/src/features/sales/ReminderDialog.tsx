import { Check, Copy, MessageCircle } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Textarea } from '@/components/ui/textarea'
import { useAuth } from '@/features/auth/auth-context'
import type { InvoiceListItem } from './api'
import { reminderMessages } from './reminders'

function Message({ customer, initial }: { customer: string; initial: string }) {
  const [text, setText] = useState(initial)
  const [copied, setCopied] = useState(false)
  return (
    <section className="space-y-2 rounded-xl border p-3">
      <h3 className="text-sm font-medium">{customer}</h3>
      <Textarea value={text} onChange={(e) => setText(e.target.value)} rows={Math.min(12, text.split('\n').length + 1)} className="text-sm" />
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          variant="outline"
          onClick={() =>
            void navigator.clipboard.writeText(text).then(() => {
              setCopied(true)
              setTimeout(() => setCopied(false), 1500)
            })
          }
        >
          {copied ? <Check /> : <Copy />} {copied ? 'Copied' : 'Copy'}
        </Button>
        <Button size="sm" variant="outline" asChild>
          <a href={`https://wa.me/?text=${encodeURIComponent(text)}`} target="_blank" rel="noreferrer">
            <MessageCircle /> Open in WhatsApp
          </a>
        </Button>
      </div>
    </section>
  )
}

/**
 * Payment reminders for the ticked invoices, one per customer, ready to edit and send yourself: copy into an email,
 * or open WhatsApp with the text filled in. The app never sends anything itself.
 */
export function ReminderDialog({ invoices, onOpenChange }: { invoices: InvoiceListItem[] | null; onOpenChange: (open: boolean) => void }) {
  const { user } = useAuth()
  const messages = invoices ? reminderMessages(invoices, user?.fullName ?? '') : []

  return (
    <Dialog open={invoices !== null} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Payment reminders</DialogTitle>
          <DialogDescription>
            One message per customer for the ticked invoices with something still due. Edit them if you like, then copy or open WhatsApp: you send them,
            the app doesn't.
          </DialogDescription>
        </DialogHeader>
        {messages.length === 0 ? (
          <p className="text-sm text-muted-foreground">None of the ticked invoices has anything due.</p>
        ) : (
          <div className="space-y-3">
            {messages.map((message) => (
              <Message key={message.customer} customer={message.customer} initial={message.text} />
            ))}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
