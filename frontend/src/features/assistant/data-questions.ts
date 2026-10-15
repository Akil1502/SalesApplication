import type { AssistantItem } from './api'

/**
 * Finds the finished turns that only looked up figures: one or more sales-data queries, no customer or document
 * lookups and no draft. Ask your data answers those better (a table and chart drawn straight from the rows, with
 * Excel export), so the panel offers to open the same question there. Returns the index of each such turn's last
 * item, mapped to the question that started it.
 */
export function dataQuestionTurns(items: AssistantItem[], replying: boolean): Map<number, string> {
  const found = new Map<number, string>()
  let start = -1

  const close = (end: number) => {
    if (start < 0 || end <= start) return
    const turn = items.slice(start + 1, end + 1)
    const tools = turn.flatMap((item) => (item.tool ? [item.tool] : []))
    const last = turn.at(-1)
    const onlyData = tools.length > 0 && tools.every((tool) => tool.name === 'query_sales_data') && tools.some((tool) => tool.succeeded)
    const answered = last?.kind === 'Assistant' && !last.interrupted
    if (onlyData && answered && !turn.some((item) => item.kind === 'Draft')) found.set(end, items[start]?.text ?? '')
  }

  items.forEach((item, i) => {
    if (item.kind !== 'User') return
    close(i - 1)
    start = i
  })
  if (!replying) close(items.length - 1)
  return found
}
