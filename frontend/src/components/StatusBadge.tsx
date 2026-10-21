import { Badge } from '@/components/ui/badge'
import { t } from '@/lib/i18n'

export function ActiveBadge({ active }: { active: boolean }) {
  return active ? (
    <Badge variant="outline" className="border-emerald-600/30 bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">
      {t('Active')}
    </Badge>
  ) : (
    <Badge variant="outline" className="text-muted-foreground">
      {t('Inactive')}
    </Badge>
  )
}
