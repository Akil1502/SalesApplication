import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AssistantDraft, Conversation, ReplyHandlers } from './api'

const api = vi.hoisted(() => ({ sendMessage: vi.fn(), loadConversation: vi.fn(), deleteConversation: vi.fn() }))
vi.mock('./api', () => api)

const usage = { model: 'claude-opus-5-5', inputTokens: 5, outputTokens: 400, cacheReadTokens: 6000, cacheWriteTokens: 300, costUsd: 0.012, costInr: 1.06, durationMs: 5200 }
const draft: AssistantDraft = {
  toolUseId: 'tu_2', kind: 'Email', purpose: 'payment_reminder', customerId: 7, customerName: 'Kavya Traders', contactPerson: 'Kavya Raman',
  email: 'accounts@kavya.example', phone: null, subject: 'Payment reminder', body: 'Dear Kavya Raman, …', warnings: [],
}

async function freshStore() {
  vi.resetModules()
  return import('./assistant-store')
}

/** A reply the test drives step by step through the handlers; aborting fails it like fetch does. */
function scriptedReply() {
  let handlers!: ReplyHandlers
  let finish = () => {}
  api.sendMessage.mockImplementation((_id: string | null, _message: string, h: ReplyHandlers, signal: AbortSignal) => {
    handlers = h
    return new Promise<void>((resolve, reject) => {
      signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
      finish = resolve
    })
  })
  return { handlers: () => handlers, finish: () => finish() }
}

describe('assistant store', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.clearAllMocks())

  it('builds the conversation from the streamed reply', async () => {
    const { assistantStore } = await freshStore()
    const reply = scriptedReply()

    const sending = assistantStore.send('Draft a reminder for Kavya Traders')
    expect(assistantStore.getSnapshot()).toMatchObject({ status: 'replying', items: [{ kind: 'User', text: 'Draft a reminder for Kavya Traders' }] })

    const h = reply.handlers()
    h.onStarted('c-1', 'Draft a reminder for Kavya Traders')
    h.onText('Let me ')
    h.onText('check.')
    h.onTool({ id: 'tu_1', name: 'list_invoices', label: 'Checking invoices', succeeded: null, detail: null })
    h.onTool({ id: 'tu_1', name: 'list_invoices', label: 'Listed outstanding invoices for Kavya Traders', succeeded: true, detail: null })
    h.onDraft(draft)
    h.onText('Here is the draft.')
    h.onDone(usage, null)
    reply.finish()
    await sending

    const state = assistantStore.getSnapshot()
    expect(state).toMatchObject({ conversationId: 'c-1', status: 'idle', costInr: 1.06, lastCostInr: 1.06, error: null })
    expect(state.items.map((i) => i.kind)).toEqual(['User', 'Assistant', 'Tool', 'Draft', 'Assistant'])
    expect(state.items[1]?.text).toBe('Let me check.')
    expect(state.items[2]?.tool).toMatchObject({ label: 'Listed outstanding invoices for Kavya Traders', succeeded: true })
    expect(state.items[4]?.text).toBe('Here is the draft.')
    expect(localStorage.getItem('salesapp.assistant.conversation')).toBe('c-1')
  })

  it('Stop keeps what was shown and marks it as stopped', async () => {
    const { assistantStore } = await freshStore()
    const reply = scriptedReply()

    const sending = assistantStore.send('Tell me everything')
    reply.handlers().onText('Here is a long')
    assistantStore.stop()
    await sending

    expect(assistantStore.getSnapshot().status).toBe('idle')
    expect(assistantStore.getSnapshot().items.at(-1)).toMatchObject({ kind: 'Assistant', text: 'Here is a long', interrupted: true })
  })

  it('shows an error the server sends', async () => {
    const { assistantStore } = await freshStore()
    const reply = scriptedReply()

    const sending = assistantStore.send('Something')
    reply.handlers().onError('Claude declined to answer that message.')
    reply.finish()
    await sending

    expect(assistantStore.getSnapshot()).toMatchObject({ status: 'idle', error: 'Claude declined to answer that message.' })
  })

  it('reopens the last conversation after a reload', async () => {
    localStorage.setItem('salesapp.assistant.conversation', 'c-9')
    const conversation: Conversation = { id: 'c-9', title: 'Overdue customers', notice: null, costInr: 4.2, items: [{ kind: 'User', text: 'Who is overdue?', tool: null, draft: null, interrupted: false }] }
    api.loadConversation.mockResolvedValue(conversation)
    const { assistantStore } = await freshStore()

    await assistantStore.resume()

    expect(api.loadConversation).toHaveBeenCalledWith('c-9')
    expect(assistantStore.getSnapshot()).toMatchObject({ conversationId: 'c-9', title: 'Overdue customers', costInr: 4.2, status: 'idle' })
  })

  it('forgets a conversation that no longer exists', async () => {
    localStorage.setItem('salesapp.assistant.conversation', 'gone')
    const { assistantStore } = await freshStore()
    const { ApiRequestError } = await import('@/lib/api/errors') // the same module instance the store uses
    api.loadConversation.mockRejectedValue(new ApiRequestError(404, { message: 'Not found', traceId: '' }))

    await assistantStore.resume()

    expect(assistantStore.getSnapshot()).toMatchObject({ conversationId: null, items: [], error: null })
    expect(localStorage.getItem('salesapp.assistant.conversation')).toBeNull()
  })

  it('clears everything on sign-out', async () => {
    const { assistantStore } = await freshStore()
    const { authStore } = await import('@/features/auth/auth-store')
    authStore.set({ token: 't', expiresAtUtc: new Date(Date.now() + 3600e3).toISOString() })
    const reply = scriptedReply()
    const sending = assistantStore.send('Hello')
    reply.handlers().onStarted('c-2', 'Hello')

    authStore.clear()
    await sending

    expect(assistantStore.getSnapshot()).toMatchObject({ conversationId: null, items: [], status: 'idle' })
    expect(localStorage.getItem('salesapp.assistant.conversation')).toBeNull()
  })
})
