import { describe, expect, it } from 'vitest'
import type { AssistantItem, AssistantTool } from './api'
import { dataQuestionTurns } from './data-questions'

const user = (text: string): AssistantItem => ({ kind: 'User', text, tool: null, draft: null, interrupted: false })
const reply = (text = 'Here it is.', interrupted = false): AssistantItem => ({ kind: 'Assistant', text, tool: null, draft: null, interrupted })
const tool = (name: string, succeeded: boolean | null = true): AssistantItem => ({
  kind: 'Tool',
  text: null,
  tool: { id: `${name}-${Math.random()}`, name, label: name, succeeded, detail: null } as AssistantTool,
  draft: null,
  interrupted: false,
})
const draft: AssistantItem = { kind: 'Draft', text: null, tool: null, draft: null, interrupted: false }

describe('dataQuestionTurns', () => {
  it('offers Ask your data for a turn that only queried sales data', () => {
    const items = [user('Which products are low on stock?'), tool('query_sales_data'), reply()]
    expect(dataQuestionTurns(items, false)).toEqual(new Map([[2, 'Which products are low on stock?']]))
  })

  it('skips turns with other lookups, drafts or no lookups at all', () => {
    const items = [
      user('How is Murugan Garments doing?'),
      tool('get_customer'),
      tool('query_sales_data'),
      reply(),
      user('Draft a reminder'),
      tool('query_sales_data'),
      draft,
      reply(),
      user('Thanks'),
      reply('You are welcome.'),
      user('Monthly sales for the last 12 months'),
      tool('query_sales_data'),
      tool('query_sales_data'),
      reply(),
    ]
    expect(dataQuestionTurns(items, false)).toEqual(new Map([[13, 'Monthly sales for the last 12 months']]))
  })

  it('waits for the reply to finish, and skips stopped or failed ones', () => {
    const asking = [user('Sales by state'), tool('query_sales_data', null)]
    expect(dataQuestionTurns(asking, true).size).toBe(0)

    expect(dataQuestionTurns([user('Sales by state'), tool('query_sales_data'), reply('Half', true)], false).size).toBe(0)
    expect(dataQuestionTurns([user('Sales by state'), tool('query_sales_data', false), reply()], false).size).toBe(0)
  })

  it('still offers earlier turns while a new reply is streaming', () => {
    const items = [user('Sales by state'), tool('query_sales_data'), reply(), user('And by category?'), tool('query_sales_data', null)]
    expect(dataQuestionTurns(items, true)).toEqual(new Map([[2, 'Sales by state']]))
  })
})
