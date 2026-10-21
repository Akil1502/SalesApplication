import { Fragment } from 'react'
import { Checkbox } from '@/components/ui/checkbox'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn } from '@/lib/utils'
import type { PermissionCatalog } from './api'
import { toggle } from './permission-logic'
import { t, tc } from '@/lib/i18n'

/** One box. changedFrom is what the user's role has when this user's box differs from it, else null. */
function PermissionBox({
  checked,
  changedFrom,
  label,
  description,
  onCheckedChange,
}: {
  checked: boolean
  changedFrom: boolean | null
  label: string
  description: string
  onCheckedChange?: (on: boolean) => void
}) {
  const changed = changedFrom !== null
  return (
    <span
      className={cn('inline-flex rounded-md p-1.5', changed && 'bg-amber-100 ring-1 ring-amber-400 dark:bg-amber-950/60 dark:ring-amber-600')}
      title={
        changed
          ? `${description} ${changedFrom ? t('Changed for this user: their role has it.') : t("Changed for this user: their role doesn't have it.")}`
          : description
      }
    >
      <Checkbox checked={checked} disabled={!onCheckedChange} aria-label={label} onCheckedChange={(next) => onCheckedChange?.(next === true)} />
    </span>
  )
}

interface PermissionGridProps {
  catalog: PermissionCatalog
  value: ReadonlySet<string>
  /** Leave out for a read-only grid. */
  onChange?: (next: Set<string>) => void
  /** A user's role permissions: cells that differ from it are marked as changed for this user. */
  baseline?: ReadonlySet<string>
}

/**
 * Every menu with its View and Add & change boxes, grouped as the sidebar is, then the special rights. Ticking a box
 * ticks what it needs; unticking one unticks what depends on it (the server applies the same rules).
 */
export function PermissionGrid({ catalog, value, onChange, baseline }: PermissionGridProps) {
  const sections = [...new Set(catalog.menus.map((m) => m.section))]

  const box = (permission: string, label: string, description: string) => (
    <PermissionBox
      checked={value.has(permission)}
      changedFrom={baseline === undefined || value.has(permission) === baseline.has(permission) ? null : baseline.has(permission)}
      label={label}
      description={description}
      onCheckedChange={onChange && ((on) => onChange(toggle(value, permission, on, catalog.needs)))}
    />
  )

  return (
    <div className="flex flex-col gap-4">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t('Menu')}</TableHead>
            <TableHead className="w-20 text-center">{tc('permission', 'View')}</TableHead>
            <TableHead className="w-28 text-center">{t('Add & change')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {sections.map((section) => (
            <Fragment key={section}>
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={3} className="pt-4 pb-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                  {section}
                </TableCell>
              </TableRow>
              {catalog.menus
                .filter((m) => m.section === section)
                .map((menu) => (
                  <TableRow key={menu.view}>
                    <TableCell className="whitespace-normal">
                      <div className="font-medium">{menu.name}</div>
                      <div className="text-xs text-muted-foreground">{menu.viewDescription}</div>
                    </TableCell>
                    <TableCell className="text-center">
                      {box(menu.view, t('{menu}: view', { menu: menu.name }), menu.viewDescription)}
                    </TableCell>
                    <TableCell className="text-center">
                      {menu.edit ? (
                        box(menu.edit, t('{menu}: add and change', { menu: menu.name }), menu.editDescription ?? '')
                      ) : (
                        <span className="text-muted-foreground" aria-hidden>
                          –
                        </span>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
            </Fragment>
          ))}
        </TableBody>
      </Table>

      <div className="flex flex-col gap-1">
        <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t('Special rights')}</p>
        {catalog.specialRights.map((right) => (
          <label key={right.key} className="flex items-start gap-2 rounded-md py-1">
            {box(right.key, right.name, right.description)}
            <span className="pt-1">
              <span className="block text-sm font-medium">{right.name}</span>
              <span className="block text-xs text-muted-foreground">{right.description}</span>
            </span>
          </label>
        ))}
      </div>
    </div>
  )
}
