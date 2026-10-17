import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { useContext, useEffect, useRef, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import { EmptyArt } from '@/components/fx/EmptyArt'
import { ListRowsContext, setMorphOrigin, useInPanel, useOpenRecordId } from '@/components/layout/record-panel-state'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { errorMessage } from '@/lib/api/errors'
import { isTyping, overlayOpen, plainKey } from '@/lib/keyboard'
import { cn } from '@/lib/utils'

export interface Column<T> {
  key: string
  header: string
  cell: (row: T) => ReactNode
  /** Server-side sort key; omit for unsortable columns. */
  sortKey?: string
  className?: string
  align?: 'left' | 'right'
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
}

/**
 * A server-driven table: loading skeleton, error and empty states, optional sorting and row click. Clickable rows can
 * be reached with Tab and opened with Enter, and J/K move along them; inside a list with a side panel, the table tells
 * the panel its rows so the panel can step through them.
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
}: DataTableProps<T>) {
  const openId = useOpenRecordId()
  const inPanel = useInPanel()
  const reportRows = useContext(ListRowsContext)
  const body = useRef<HTMLTableSectionElement>(null)
  const keys = rows && !isLoading ? rows.map((row) => String(getRowKey(row))) : []
  const keyList = keys.join('|')

  // The panel beside this list steps through these rows (only the list's own table, not one inside the panel).
  useEffect(() => {
    if (!inPanel && onRowClick) reportRows?.(keyList ? keyList.split('|') : [])
  }, [keyList, inPanel, onRowClick, reportRows])

  // J/K move along the rows and Enter opens one. While a record is open the panel steps instead.
  useEffect(() => {
    if (inPanel || !onRowClick) return
    const onKey = (event: KeyboardEvent) => {
      if (openId !== undefined || !plainKey(event) || isTyping(event) || overlayOpen()) return
      if (event.key !== 'j' && event.key !== 'k') return
      const rowElements = Array.from(body.current?.querySelectorAll<HTMLElement>('tr[data-row-key]') ?? [])
      if (rowElements.length === 0) return
      const current = rowElements.indexOf(document.activeElement as HTMLElement)
      const next = current < 0 ? 0 : Math.min(Math.max(current + (event.key === 'j' ? 1 : -1), 0), rowElements.length - 1)
      event.preventDefault()
      rowElements[next]?.focus()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [inPanel, onRowClick, openId])

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

  return (
    <div className="overflow-hidden rounded-xl border bg-card shadow-[var(--shadow-elevated)]">
      <Table>
        <TableHeader className="bg-muted/50">
          <TableRow>
            {columns.map((column) => (
              <TableHead key={column.key} className={cn(column.align === 'right' && 'text-right', column.className)}>
                {column.sortKey && onSortChange ? (
                  <button
                    type="button"
                    className={cn(
                      'inline-flex items-center gap-1 hover:text-foreground',
                      column.align === 'right' && 'flex-row-reverse',
                    )}
                    onClick={() =>
                      onSortChange(activeKey === column.sortKey && !descending ? `-${column.sortKey}` : column.sortKey!)
                    }
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
                {columns.map((column) => (
                  <TableCell key={column.key}>
                    <Skeleton className="h-4 w-full max-w-40" />
                  </TableCell>
                ))}
              </TableRow>
            ))}

          {!isLoading && rows?.length === 0 && (
            <TableRow className="hover:bg-transparent">
              <TableCell colSpan={columns.length}>
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
            rows?.map((row) => (
              <TableRow
                key={getRowKey(row)}
                data-row-key={onRowClick ? String(getRowKey(row)) : undefined}
                className={cn(onRowClick && 'cursor-pointer outline-none focus-visible:bg-accent focus-visible:shadow-[inset_3px_0_0_var(--brand)]')}
                // The row whose record is open in the side panel stays marked.
                data-state={openId !== undefined && String(getRowKey(row)) === openId ? 'selected' : undefined}
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
                {columns.map((column) => (
                  <TableCell key={column.key} className={cn(column.align === 'right' && 'text-right tabular-nums', column.className)}>
                    {column.cell(row)}
                  </TableCell>
                ))}
              </TableRow>
            ))}
        </TableBody>
      </Table>
    </div>
  )
}
