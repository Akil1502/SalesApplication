import { useSyncExternalStore } from 'react'
import { authStore } from '@/features/auth/auth-store'
import { ApiRequestError, errorMessage } from '@/lib/api/errors'
import { deleteConversation, loadConversation, sendMessage, type AssistantItem } from './api'

export interface AssistantState {
  conversationId: string | null
  title: string | null
  items: AssistantItem[]
  status: 'idle' | 'loading' | 'replying'
  error: string | null
  notice: string | null
  /** What the whole conversation has cost so far, in rupees. */
  costInr: number
  /** What the last reply cost. */
  lastCostInr: number | null
}

const CURRENT_KEY = 'salesapp.assistant.conversation'

const empty: AssistantState = {
  conversationId: null,
  title: null,
  items: [],
  status: 'idle',
  error: null,
  notice: null,
  costInr: 0,
  lastCostInr: null,
}

/**
 * The chat lives here rather than in the panel, so a reply keeps streaming while the panel is closed or the user
 * moves between screens. The open conversation's id is remembered in localStorage, so a reload resumes it; the
 * conversation itself is always loaded from the server, which stores every message.
 */
let state: AssistantState = empty
let controller: AbortController | null = null
let resumed = false
const listeners = new Set<() => void>()

function set(change: Partial<AssistantState> | ((s: AssistantState) => Partial<AssistantState>)) {
  state = { ...state, ...(typeof change === 'function' ? change(state) : change) }
  listeners.forEach((listener) => listener())
}

function remember(id: string | null) {
  try {
    if (id) localStorage.setItem(CURRENT_KEY, id)
    else localStorage.removeItem(CURRENT_KEY)
  } catch {
    // Storage blocked: the conversation just won't reopen after a reload.
  }
}

function remembered(): string | null {
  try {
    return localStorage.getItem(CURRENT_KEY)
  } catch {
    return null
  }
}

/** Appends streamed text to the reply being written, or starts a new reply after a lookup or draft. */
function appendText(items: AssistantItem[], delta: string): AssistantItem[] {
  const last = items.at(-1)
  if (last?.kind === 'Assistant') return [...items.slice(0, -1), { ...last, text: (last.text ?? '') + delta }]
  return [...items, { kind: 'Assistant', text: delta, tool: null, draft: null, interrupted: false }]
}

export const assistantStore = {
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },

  getSnapshot: () => state,

  /** Reopens the conversation that was open before a reload (once per page load). */
  async resume() {
    if (resumed) return
    resumed = true
    const id = remembered()
    if (id && !state.conversationId && state.items.length === 0) await assistantStore.open(id)
  },

  async open(id: string) {
    if (state.status === 'replying') return
    set({ ...empty, conversationId: id, status: 'loading' })
    try {
      const conversation = await loadConversation(id)
      if (state.conversationId !== id) return // the user moved on while it loaded
      set({ ...empty, conversationId: id, title: conversation.title, items: conversation.items, notice: conversation.notice, costInr: conversation.costInr })
      remember(id)
    } catch (error) {
      if (state.conversationId !== id) return
      const gone = error instanceof ApiRequestError && error.status === 404
      if (gone) remember(null)
      set({ ...empty, error: gone ? null : errorMessage(error) })
    }
  },

  newChat() {
    controller?.abort()
    remember(null)
    set(empty)
  },

  async send(text: string) {
    const message = text.trim()
    if (!message || state.status !== 'idle') return
    const ownController = new AbortController()
    controller = ownController
    set((s) => ({
      status: 'replying',
      error: null,
      lastCostInr: null,
      items: [...s.items, { kind: 'User', text: message, tool: null, draft: null, interrupted: false }],
    }))

    try {
      await sendMessage(
        state.conversationId,
        message,
        {
          onStarted: (id, title) => {
            set({ conversationId: id, title })
            remember(id)
          },
          onText: (delta) => set((s) => ({ items: appendText(s.items, delta) })),
          onTool: (tool) =>
            set((s) => {
              const index = s.items.findIndex((i) => i.tool?.id === tool.id)
              const item: AssistantItem = { kind: 'Tool', text: null, tool, draft: null, interrupted: false }
              return { items: index < 0 ? [...s.items, item] : s.items.map((existing, i) => (i === index ? item : existing)) }
            }),
          onDraft: (draft) => set((s) => ({ items: [...s.items, { kind: 'Draft', text: null, tool: null, draft, interrupted: false }] })),
          onDone: (usage, notice) => set((s) => ({ status: 'idle', notice, costInr: s.costInr + usage.costInr, lastCostInr: usage.costInr })),
          onError: (error) => set({ status: 'idle', error }),
        },
        ownController.signal,
      )
      // Read through the getter: the handlers above changed the state while this function waited.
      if (assistantStore.getSnapshot().status === 'replying') set({ status: 'idle', error: 'The reply was cut off. Try sending your message again.' })
    } catch (error) {
      if (ownController.signal.aborted) {
        // Stopped: the server keeps what had streamed, marked as stopped; show it the same way.
        set((s) => {
          const last = s.items.at(-1)
          return {
            status: 'idle',
            items: last?.kind === 'Assistant' ? [...s.items.slice(0, -1), { ...last, interrupted: true }] : s.items,
          }
        })
      } else {
        set({ status: 'idle', error: errorMessage(error) })
      }
    } finally {
      if (controller === ownController) controller = null
    }
  },

  /** The Stop button: closes the stream, so Claude stops writing (and billing) the reply. */
  stop() {
    controller?.abort()
  },

  async remove(id: string) {
    await deleteConversation(id)
    if (state.conversationId === id) assistantStore.newChat()
  },
}

// Signing out must not leave one person's conversation on screen for the next person to sign in on this tab.
authStore.subscribe(() => {
  if (!authStore.getSession()) {
    controller?.abort()
    remember(null)
    resumed = false
    set(empty)
  }
})

export function useAssistant() {
  return useSyncExternalStore(assistantStore.subscribe, assistantStore.getSnapshot)
}
