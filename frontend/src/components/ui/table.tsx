import * as React from "react"
import { cn } from "cn"

/**
 * stack: line-item tables that turn into a card per row when the page (or the side panel) is narrow. Each cell is
 * labelled with its column heading for that view, kept up to date as rows come and go (see index.css).
 */
function Table({ className, stack = false, ...props }: React.ComponentProps<"table"> & { stack?: boolean }) {
  const ref = React.useRef<HTMLTableElement>(null)

  React.useLayoutEffect(() => {
    const table = ref.current
    if (!stack || !table) return
    const label = () => {
      const headings = Array.from(table.tHead?.rows[0]?.cells ?? []).flatMap((cell) =>
        Array<string>(cell.colSpan || 1).fill(cell.textContent?.trim() ?? "")
      )
      for (const body of Array.from(table.tBodies)) {
        for (const row of Array.from(body.rows)) {
          let column = 0
          for (const cell of Array.from(row.cells)) {
            const heading = cell.colSpan > 1 ? "" : headings[column]
            if (heading) cell.dataset.label = heading
            else delete cell.dataset.label
            column += cell.colSpan || 1
          }
        }
      }
    }
    label()
    const observer = new MutationObserver(label)
    observer.observe(table, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [stack])

  return (
    <div
      data-slot="table-container"
      className="relative w-full overflow-x-auto"
    >
      <table
        ref={ref}
        data-slot="table"
        data-stack={stack ? "" : undefined}
        className={cn("w-full caption-bottom text-sm", className)}
        {...props}
      />
    </div>
  )
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
  return (
    <thead
      data-slot="table-header"
      className={cn("[&_tr]:border-b", className)}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn("[&_tr:last-child]:border-0", className)}
      {...props}
    />
  )
}

function TableFooter({ className, ...props }: React.ComponentProps<"tfoot">) {
  return (
    <tfoot
      data-slot="table-footer"
      className={cn(
        "border-t bg-muted/50 font-medium [&>tr]:last:border-b-0",
        className
      )}
      {...props}
    />
  )
}

function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
  return (
    <tr
      data-slot="table-row"
      className={cn(
        "border-b transition-colors hover:bg-muted/50 has-aria-expanded:bg-muted/50 data-[state=selected]:bg-muted",
        className
      )}
      {...props}
    />
  )
}

function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-10 px-2 text-left align-middle font-medium whitespace-nowrap text-foreground [&:has([role=checkbox])]:pr-0",
        className
      )}
      {...props}
    />
  )
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
  return (
    <td
      data-slot="table-cell"
      className={cn(
        "p-2 align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0",
        className
      )}
      {...props}
    />
  )
}

function TableCaption({
  className,
  ...props
}: React.ComponentProps<"caption">) {
  return (
    <caption
      data-slot="table-caption"
      className={cn("mt-4 text-sm text-muted-foreground", className)}
      {...props}
    />
  )
}

export {
  Table,
  TableHeader,
  TableBody,
  TableFooter,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
}
