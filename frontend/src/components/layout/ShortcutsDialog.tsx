import { Keyboard } from 'lucide-react'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { t } from '@/lib/i18n'
import { SHORTCUT_GROUPS } from './shortcuts'

function Keys({ keys }: { keys: string[] }) {
  return (
    <span className="flex shrink-0 items-center gap-1">
      {keys.map((key, i) => (
        <kbd key={i} className="min-w-6 rounded-md border bg-muted px-1.5 py-0.5 text-center font-mono text-[11px] shadow-[inset_0_-1px_0_var(--border)]">
          {key}
        </kbd>
      ))}
    </span>
  )
}

/** Every keyboard shortcut, opened with "?" or from the account menu. */
export function ShortcutsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Keyboard className="size-5 text-brand" /> {t('Keyboard shortcuts')}
          </DialogTitle>
          <DialogDescription>{t("Letter keys work whenever you're not typing in a field.")}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-6 sm:grid-cols-2">
          {SHORTCUT_GROUPS.map((group) => (
            <section key={group.title} className="space-y-2">
              <h3 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t(group.title)}</h3>
              <ul className="space-y-1.5">
                {group.items.map((item) => (
                  <li key={item.description} className="flex items-center justify-between gap-3 text-sm">
                    <span>{t(item.description)}</span>
                    <Keys keys={item.keys} />
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  )
}
