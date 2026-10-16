import { Copy, Mail, MessageCircle, Send, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import type { AssistantDraft } from './api'
import { draftText, mailtoUrl, whatsappUrl } from './drafts'

const purposes: Record<string, string> = {
  payment_reminder: 'Payment reminder',
  follow_up: 'Follow-up',
  quotation: 'Quotation',
  order_confirmation: 'Order confirmation',
  thank_you: 'Thank-you',
  delivery_follow_up: 'Delivery follow-up',
  bill_query: 'Bill query',
  credit_note: 'Credit note',
  other: 'Message',
}

/**
 * A drafted email or WhatsApp message. The user can edit it, copy it, or open it in their own email or WhatsApp;
 * nothing is sent from SalesApp. The recipient is the customer's contact from the customer record.
 */
export function DraftCard({ draft }: { draft: AssistantDraft }) {
  const [subject, setSubject] = useState(draft.subject ?? '')
  const [body, setBody] = useState(draft.body)
  const email = draft.kind === 'Email'
  const recipient = email ? draft.email : draft.phone
  const openUrl = recipient ? (email ? mailtoUrl(recipient, subject || null, body) : whatsappUrl(recipient, body)) : null

  async function copy() {
    try {
      await navigator.clipboard.writeText(draftText(email ? subject || null : null, body))
      toast.success('Copied. Paste it into your email or WhatsApp.')
    } catch {
      toast.error("Couldn't copy. Select the text and copy it yourself.")
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border bg-card p-3 shadow-xs">
      <div className="flex items-start gap-2">
        <span className="mt-0.5 shrink-0 text-muted-foreground">{email ? <Mail className="size-4" /> : <MessageCircle className="size-4" />}</span>
        <div className="min-w-0 text-sm">
          <p className="font-medium">
            {purposes[draft.purpose] ?? 'Message'} · {email ? 'email' : 'WhatsApp'} draft
          </p>
          <p className="text-muted-foreground">
            To {draft.contactPerson ? `${draft.contactPerson}, ` : ''}
            {draft.recipientName}
            {recipient ? <span className="break-all"> ({recipient})</span> : <span> — no {email ? 'email address' : 'phone number'} on file</span>}
          </p>
        </div>
      </div>

      {draft.warnings.length > 0 && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <TriangleAlert />
          <AlertDescription className="text-inherit">
            <p className="font-medium">Check before sending:</p>
            <ul className="ml-4 list-disc">
              {draft.warnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {email && (
        <label className="flex flex-col gap-1 text-xs font-medium text-muted-foreground">
          Subject
          <input
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            className="h-8 rounded-md border bg-background px-2 text-sm font-normal text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
          />
        </label>
      )}
      <Textarea aria-label="Message" value={body} onChange={(e) => setBody(e.target.value)} className="min-h-40 text-sm leading-relaxed" />

      <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" variant="outline" onClick={() => void copy()}>
          <Copy /> Copy
        </Button>
        {openUrl && (
          <Button size="sm" variant="outline" asChild>
            <a href={openUrl} target={email ? undefined : '_blank'} rel="noopener noreferrer">
              <Send /> {email ? 'Open in email' : 'Open WhatsApp'}
            </a>
          </Button>
        )}
        <span className="text-xs text-muted-foreground">Nothing is sent until you send it.</span>
      </div>
    </div>
  )
}
