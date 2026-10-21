import { useBlocker } from 'react-router'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { t } from '@/lib/i18n'

/** Asks before navigating away from a form with unsaved edits. */
export function UnsavedChangesGuard({ when }: { when: boolean }) {
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => when && currentLocation.pathname !== nextLocation.pathname,
  )

  return (
    <ConfirmDialog
      open={blocker.state === 'blocked'}
      onOpenChange={(open) => !open && blocker.state === 'blocked' && blocker.reset()}
      title={t('Discard unsaved changes?')}
      description={t("You've changed this form but haven't saved. If you leave now, those changes are lost.")}
      confirmLabel={t('Discard changes')}
      destructive
      onConfirm={() => blocker.state === 'blocked' && blocker.proceed()}
    />
  )
}
