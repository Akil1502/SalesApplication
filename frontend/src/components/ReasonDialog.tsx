import { useId, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import { t } from '@/lib/i18n'

interface ReasonDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  confirmLabel: string
  onConfirm: (reason: string) => Promise<void>
  /** What the text box is for; "Reason" unless given. */
  label?: string
  /** Shown when the box is left empty. */
  requiredMessage?: string
  cancelLabel?: string
  /** False for an action that isn't destructive (approving), so the button isn't red. */
  destructive?: boolean
}

/** Asks for a reason before a destructive action (cancelling an order or invoice), or a note with an approval. */
export function ReasonDialog({ open, onOpenChange, ...props }: ReasonDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* Content unmounts when the dialog closes, so the form starts empty every time it opens. */}
      <DialogContent>
        <ReasonForm {...props} onCancel={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function ReasonForm({
  title,
  description,
  confirmLabel,
  onConfirm,
  onCancel,
  label = t('Reason'),
  requiredMessage = t('Give a reason.'),
  cancelLabel = t('Keep it'),
  destructive = true,
}: Omit<ReasonDialogProps, 'open' | 'onOpenChange'> & { onCancel: () => void }) {
  const id = useId()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    // Dialogs render in a portal, but React still bubbles events to the parent component tree.
    e.stopPropagation()
    if (!reason.trim()) {
      setError(requiredMessage)
      return
    }
    setBusy(true)
    try {
      await onConfirm(reason.trim())
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
      <Field data-invalid={!!error} className="py-4">
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
        <Textarea id={id} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} aria-invalid={!!error} autoFocus />
        <FieldError errors={error ? [{ message: error }] : []} />
      </Field>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onCancel}>
          {cancelLabel}
        </Button>
        <Button type="submit" variant={destructive ? 'destructive' : 'default'} disabled={busy}>
          {busy && <Spinner />}
          {confirmLabel}
        </Button>
      </DialogFooter>
    </form>
  )
}
