import { Badge } from '@/components/ui/badge'
import type { AuditAction } from './api'
import { actionLabels, isWarning } from './audit-format'

/** An audit action as a badge: failed sign-ins and lockouts stand out. */
export function ActionBadge({ action }: { action: AuditAction }) {
  return (
    <Badge variant={isWarning(action) ? 'destructive' : action === 'Deleted' ? 'outline' : 'secondary'} className="whitespace-nowrap">
      {actionLabels[action]}
    </Badge>
  )
}
