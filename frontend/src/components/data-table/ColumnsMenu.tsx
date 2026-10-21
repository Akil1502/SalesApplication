import { Columns3, RotateCcw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { t } from '@/lib/i18n'
import type { Density } from './table-settings'

interface ColumnsMenuProps {
  columns: { key: string; header: string }[]
  hidden: string[]
  onToggle: (key: string) => void
  density: Density
  onDensity: (density: Density) => void
  onReset: () => void
}

/** Which columns a list shows and how tightly its rows sit. The first column always stays. */
export function ColumnsMenu({ columns, hidden, onToggle, density, onDensity, onReset }: ColumnsMenuProps) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="bg-card">
          <Columns3 />
          {t('Columns')}
          {hidden.length > 0 && <span className="rounded-full bg-brand px-1.5 text-[10px] text-white">{t('{count} hidden', { count: hidden.length })}</span>}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-52">
        <DropdownMenuLabel className="text-xs text-muted-foreground">{t('Show columns')}</DropdownMenuLabel>
        {columns.map((column, index) => (
          <DropdownMenuCheckboxItem
            key={column.key}
            checked={!hidden.includes(column.key)}
            disabled={index === 0}
            onSelect={(event) => event.preventDefault()}
            onCheckedChange={() => onToggle(column.key)}
          >
            {column.header ? t(column.header) : t('Actions')}
          </DropdownMenuCheckboxItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuLabel className="text-xs text-muted-foreground">{t('Rows')}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={density} onValueChange={(value) => onDensity(value as Density)}>
          <DropdownMenuRadioItem value="comfortable">{t('Comfortable')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="compact">{t('Compact')}</DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={onReset}>
          <RotateCcw />
          {t('Back to the standard layout')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
