import { ArrowDown, ArrowUp, ArrowUpDown, Inbox } from 'lucide-react'
import type { ReactNode } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { errorMessage } from '@/lib/api/errors'
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

/** A server-driven table: loading skeleton, error and empty states, optional sorting and row click. */
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
    <div className="overflow-hidden rounded-lg border">
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
        <TableBody>
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
                    <EmptyMedia variant="icon">
                      <Inbox />
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
                className={cn(onRowClick && 'cursor-pointer')}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
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
