import { Copy, Mail, MessageCircle, Send, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { msg, t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'
import type { AssistantDraft } from './api'
import { draftText, mailtoUrl, whatsappUrl } from './drafts'

const purposes: Record<string, string> = {
  payment_reminder: msg('Payment reminder'),
  follow_up: msg('Follow-up'),
  quotation: msg('Quotation'),
  order_confirmation: msg('Order confirmation'),
  thank_you: msg('Thank-you'),
  delivery_follow_up: msg('Delivery follow-up'),
  bill_query: msg('Bill query'),
  credit_note: msg('Credit note'),
  other: msg('Message'),
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
  const purpose = t(purposes[draft.purpose] ?? msg('Message'))
  const to = draft.contactPerson ? `${draft.contactPerson}, ${draft.recipientName}` : draft.recipientName

  async function copy() {
    try {
      await navigator.clipboard.writeText(draftText(email ? subject || null : null, body))
      toast.success(t('Copied. Paste it into your email or WhatsApp.'))
    } catch {
      toast.error(t("Couldn't copy. Select the text and copy it yourself."))
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border bg-card p-3 shadow-xs">
      <div className="flex items-start gap-2">
        <span className="mt-0.5 shrink-0 text-muted-foreground">{email ? <Mail className="size-4" /> : <MessageCircle className="size-4" />}</span>
        <div className="min-w-0 text-sm">
          <p className="font-medium">
            {email ? t('{purpose} · email draft', { purpose }) : t('{purpose} · WhatsApp draft', { purpose })}
          </p>
          <p className="text-muted-foreground">
            {recipient ? (
              <T text="To {name} {address}" values={{ name: to, address: <span className="break-all">({recipient})</span> }} />
            ) : email ? (
              t('To {name} — no email address on file', { name: to })
            ) : (
              t('To {name} — no phone number on file', { name: to })
            )}
          </p>
        </div>
      </div>

      {draft.warnings.length > 0 && (
        <Alert className="border-amber-600/30 bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <TriangleAlert />
          <AlertDescription className="text-inherit">
            <p className="font-medium">{t('Check before sending:')}</p>
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
          {t('Subject')}
          <input
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            className="h-8 rounded-md border bg-background px-2 text-sm font-normal text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
          />
        </label>
      )}
      <Textarea aria-label={t('Message')} value={body} onChange={(e) => setBody(e.target.value)} className="min-h-40 text-sm leading-relaxed" />

      <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" variant="outline" onClick={() => void copy()}>
          <Copy /> {t('Copy')}
        </Button>
        {openUrl && (
          <Button size="sm" variant="outline" asChild>
            <a href={openUrl} target={email ? undefined : '_blank'} rel="noopener noreferrer">
              <Send /> {email ? t('Open in email') : t('Open WhatsApp')}
            </a>
          </Button>
        )}
        <span className="text-xs text-muted-foreground">{t('Nothing is sent until you send it.')}</span>
      </div>
    </div>
  )
}
