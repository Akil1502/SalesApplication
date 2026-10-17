import { ArrowDown, ArrowUp, ArrowUpDown, FileSpreadsheet, X, type LucideIcon } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useContext, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import { toast } from 'sonner'
import { EmptyArt } from '@/components/fx/EmptyArt'
import { ListRowsContext, setMorphOrigin, useInPanel, useOpenRecordId } from '@/components/layout/record-panel-state'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { errorMessage } from '@/lib/api/errors'
import { todayInIndia } from '@/lib/format'
import { isTyping, overlayOpen, plainKey } from '@/lib/keyboard'
import { cn } from '@/lib/utils'
import { ColumnsMenu } from './ColumnsMenu'
import { downloadText, tableRowsCsv } from './export-csv'
import { SavedViews } from './SavedViews'
import { useTableSettings } from './table-settings'

export interface Column<T> {
  key: string
  header: string
  cell: (row: T) => ReactNode
  /** Server-side sort key; omit for unsortable columns. */
  sortKey?: string
  className?: string
  align?: 'left' | 'right'
}

/** Something to do with the ticked rows, beyond exporting them (which every list with controls offers). */
export interface BulkAction<T> {
  label: string
  icon: LucideIcon
  run: (rows: T[]) => void
  /** Why it can't run for these rows, if it can't. */
  unavailable?: (rows: T[]) => string | undefined
}

interface DataTableProps<T> {
  columns: Column<T>[]
  rows: T[] | undefined
  getRowKey: (row: T) => string | number
  isLoading: boolean
  error?: unknown
  onRowClick?: (row: T) => void
  emptyTitle?: string
  emptyDescription?: string
  sort?: string
  onSortChange?: (sort: string) => void
  /**
   * Turns on the list's controls, remembered per person under this name: saved views, choosing columns and row
   * spacing, and ticking rows to export them (plus bulkActions).
   */
  settingsKey?: string
  bulkActions?: BulkAction<T>[]
}

/**
 * A server-driven table: loading skeleton, error and empty states, optional sorting and row click. Clickable rows can
 * be reached with Tab and opened with Enter, and J/K move along them (X ticks the row); inside a list with a side
 * panel, the table tells the panel its rows so the panel can step through them.
 */
export function DataTable<T>({
  columns,
  rows,
  getRowKey,
  isLoading,
  error,
  onRowClick,
  emptyTitle = 'Nothing here yet',
  emptyDescription,
  sort,
  onSortChange,
  settingsKey,
  bulkActions = [],
}: DataTableProps<T>) {
  const openId = useOpenRecordId()
  const inPanel = useInPanel()
  const reportRows = useContext(ListRowsContext)
  const body = useRef<HTMLTableSectionElement>(null)
  const table = useRef<HTMLDivElement>(null)
  const settings = useTableSettings(settingsKey ?? 'none')
  const selectable = !!settingsKey
  const keys = rows && !isLoading ? rows.map((row) => String(getRowKey(row))) : []
  const keyList = keys.join('|')

  // Ticked rows belong to the rows they were ticked on: a new page or filter starts afresh.
  const [selection, setSelection] = useState<{ for: string; keys: ReadonlySet<string> }>({ for: '', keys: new Set() })
  const selected = selection.for === keyList ? selection.keys : new Set<string>()
  const setSelected = (next: Set<string>) => setSelection({ for: keyList, keys: next })
  const toggle = (key: string) =>
    setSelection((current) => {
      const next = new Set(current.for === keyList ? current.keys : [])
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return { for: keyList, keys: next }
    })
  const selectedRows = (rows ?? []).filter((row) => selected.has(String(getRowKey(row))))

  // The panel beside this list steps through these rows (only the list's own table, not one inside the panel).
  useEffect(() => {
    if (!inPanel && onRowClick) reportRows?.(keyList ? keyList.split('|') : [])
  }, [keyList, inPanel, onRowClick, reportRows])

  // J/K move along the rows, Enter opens one and X ticks it. While a record is open the panel steps instead.
  useEffect(() => {
    if (inPanel || !onRowClick) return
    const onKey = (event: KeyboardEvent) => {
      if (openId !== undefined || !plainKey(event) || isTyping(event) || overlayOpen()) return
      const focused = document.activeElement as HTMLElement | null
      if (event.key === 'x' && selectable && focused?.dataset.rowKey && body.current?.contains(focused)) {
        event.preventDefault()
        const key = focused.dataset.rowKey
        setSelection((current) => {
          const next = new Set(current.for === keyList ? current.keys : [])
          if (next.has(key)) next.delete(key)
          else next.add(key)
          return { for: keyList, keys: next }
        })
        return
      }
      if (event.key !== 'j' && event.key !== 'k') return
      const rowElements = Array.from(body.current?.querySelectorAll<HTMLElement>('tr[data-row-key]') ?? [])
      if (rowElements.length === 0) return
      const current = rowElements.indexOf(focused as HTMLElement)
      const next = current < 0 ? 0 : Math.min(Math.max(current + (event.key === 'j' ? 1 : -1), 0), rowElements.length - 1)
      event.preventDefault()
      rowElements[next]?.focus()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [inPanel, onRowClick, openId, selectable, keyList])

  const open = (row: T, element: HTMLElement) => {
    // The panel grows out of the row that was opened.
    setMorphOrigin(element.getBoundingClientRect())
    onRowClick?.(row)
  }

  if (error) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{errorMessage(error)}</AlertDescription>
      </Alert>
    )
  }

  const activeKey = sort?.replace(/^-/, '')
  const descending = sort?.startsWith('-') ?? false
  const visible = settingsKey ? columns.filter((column, index) => index === 0 || !settings.hidden.includes(column.key)) : columns
  const span = visible.length + (selectable ? 1 : 0)
  const allTicked = keys.length > 0 && keys.every((key) => selected.has(key))

  const exportSelected = () => {
    const element = table.current?.querySelector('table')
    if (!element || !settingsKey) return
    downloadText(tableRowsCsv(element, selected), `${settingsKey}-${todayInIndia()}.csv`)
    toast.success(`Exported ${selected.size} ${selected.size === 1 ? 'row' : 'rows'} for Excel`)
  }

  return (
    <div className="flex flex-col gap-2">
      {settingsKey && (
        <div className="flex min-h-9 flex-wrap items-center gap-2">
          <AnimatePresence mode="wait" initial={false}>
            {selected.size > 0 ? (
              <motion.div
                key="selection"
                className="flex flex-wrap items-center gap-2 rounded-lg border border-brand/30 bg-brand/8 px-2 py-1"
                initial={{ opacity: 0, y: -6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.18 }}
                role="region"
                aria-label="Ticked rows"
              >
                <span className="px-1 text-sm font-medium tabular-nums">{selected.size} ticked</span>
                <Button size="sm" variant="ghost" onClick={exportSelected}>
                  <FileSpreadsheet /> Export for Excel
                </Button>
                {bulkActions.map((action) => {
                  const reason = action.unavailable?.(selectedRows)
                  return (
                    <Button key={action.label} size="sm" variant="ghost" disabled={!!reason} title={reason} onClick={() => action.run(selectedRows)}>
                      <action.icon /> {action.label}
                    </Button>
                  )
                })}
                <Button size="sm" variant="ghost" onClick={() => setSelected(new Set())} aria-label="Untick all">
                  <X />
                </Button>
              </motion.div>
            ) : (
              <motion.div key="views" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} transition={{ duration: 0.15 }}>
                <SavedViews listKey={settingsKey} />
              </motion.div>
            )}
          </AnimatePresence>
          <div className="ml-auto">
            <ColumnsMenu
              columns={columns}
              hidden={settings.hidden}
              onToggle={settings.toggleColumn}
              density={settings.density}
              onDensity={settings.setDensity}
              onReset={settings.reset}
            />
          </div>
        </div>
      )}

      <div ref={table} className="overflow-hidden rounded-xl border bg-card shadow-[var(--shadow-elevated)]">
        <Table data-density={settingsKey ? settings.density : undefined}>
          <TableHeader className="bg-muted/50">
            <TableRow>
              {selectable && (
                <TableHead className="w-10" data-skip-export>
                  <Checkbox
                    aria-label="Tick every row on this page"
                    checked={allTicked ? true : selected.size > 0 ? 'indeterminate' : false}
                    onCheckedChange={() => setSelected(allTicked ? new Set() : new Set(keys))}
                    disabled={keys.length === 0}
                  />
                </TableHead>
              )}
              {visible.map((column) => (
                <TableHead key={column.key} className={cn(column.align === 'right' && 'text-right', column.className)}>
                  {column.sortKey && onSortChange ? (
                    <button
                      type="button"
                      className={cn('inline-flex items-center gap-1 hover:text-foreground', column.align === 'right' && 'flex-row-reverse')}
                      onClick={() => onSortChange(activeKey === column.sortKey && !descending ? `-${column.sortKey}` : column.sortKey!)}
                    >
                      {column.header}
                      {activeKey === column.sortKey ? (
                        descending ? <ArrowDown className="size-3.5" /> : <ArrowUp className="size-3.5" />
                      ) : (
                        <ArrowUpDown className="size-3.5 opacity-40" />
                      )}
                    </button>
                  ) : (
                    column.header
                  )}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody ref={body}>
            {isLoading &&
              Array.from({ length: 5 }, (_, i) => (
                <TableRow key={`skeleton-${i}`}>
                  {Array.from({ length: span }, (_, c) => (
                    <TableCell key={c}>
                      <Skeleton className="h-4 w-full max-w-40" />
                    </TableCell>
                  ))}
                </TableRow>
              ))}

            {!isLoading && rows?.length === 0 && (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={span}>
                  <Empty className="py-10">
                    <EmptyHeader>
                      <EmptyMedia>
                        <EmptyArt />
                      </EmptyMedia>
                      <EmptyTitle>{emptyTitle}</EmptyTitle>
                      {emptyDescription && <EmptyDescription>{emptyDescription}</EmptyDescription>}
                    </EmptyHeader>
                  </Empty>
                </TableCell>
              </TableRow>
            )}

            {!isLoading &&
              rows?.map((row) => {
                const key = String(getRowKey(row))
                const ticked = selected.has(key)
                return (
                  <TableRow
                    key={key}
                    data-row-key={key}
                    data-ticked={ticked || undefined}
                    className={cn(onRowClick && 'cursor-pointer outline-none focus-visible:bg-accent focus-visible:shadow-[inset_3px_0_0_var(--brand)]')}
                    // The row whose record is open in the side panel stays marked.
                    data-state={openId !== undefined && key === openId ? 'selected' : undefined}
                    tabIndex={onRowClick ? 0 : undefined}
                    onClick={onRowClick ? (event) => open(row, event.currentTarget) : undefined}
                    onKeyDown={
                      onRowClick
                        ? (event: ReactKeyboardEvent<HTMLTableRowElement>) => {
                            if ((event.key === 'Enter' || event.key === ' ') && event.target === event.currentTarget) {
                              event.preventDefault()
                              open(row, event.currentTarget)
                            }
                          }
                        : undefined
                    }
                  >
                    {selectable && (
                      // Ticking a row never opens it.
                      <TableCell data-skip-export onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
                        <Checkbox checked={ticked} onCheckedChange={() => toggle(key)} aria-label="Tick this row" />
                      </TableCell>
                    )}
                    {visible.map((column) => (
                      <TableCell key={column.key} className={cn(column.align === 'right' && 'text-right tabular-nums', column.className)}>
                        {column.cell(row)}
                      </TableCell>
                    ))}
                  </TableRow>
                )
              })}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}
