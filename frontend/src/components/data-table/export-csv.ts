/** A CSV field, quoted when it holds a comma, quote or line break. */
export function csvField(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value
}

/** Rows of text as CSV, with a byte-order mark so Excel reads ₹ and Tamil text correctly. */
export function toCsv(rows: string[][]): string {
  return '\uFEFF' + rows.map((row) => row.map(csvField).join(',')).join('\r\n')
}

/** A rupee amount on its own (₹2,82,056.00) becomes a plain number, so Excel can add it up. */
export function exportValue(text: string): string {
  const amount = /^(-?)₹\s?([\d,]+(?:\.\d+)?)$/.exec(text)
  return amount ? `${amount[1]}${amount[2]!.replace(/,/g, '')}` : text
}

function cellText(cell: HTMLTableCellElement): string {
  return exportValue((cell.innerText || cell.textContent || '').replace(/\s*\n\s*/g, ' · ').trim())
}

/**
 * The chosen rows of a table exactly as shown (the visible columns, formatted as on screen), as CSV. Cells marked
 * data-skip-export (the tick boxes) are left out.
 */
export function tableRowsCsv(table: HTMLTableElement, keys: ReadonlySet<string>): string {
  const keep = (cell: HTMLTableCellElement) => !cell.hasAttribute('data-skip-export')
  const header = Array.from(table.tHead?.rows[0]?.cells ?? []).filter(keep).map(cellText)
  const body = Array.from(table.tBodies[0]?.rows ?? [])
    .filter((row) => keys.has(row.dataset.rowKey ?? ''))
    .map((row) => Array.from(row.cells).filter(keep).map(cellText))
  return toCsv([header, ...body])
}

export function downloadText(text: string, fileName: string, type = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([text], { type }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
