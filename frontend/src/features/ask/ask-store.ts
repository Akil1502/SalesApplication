import { useSyncExternalStore } from 'react'
import { toast } from 'sonner'
import { errorMessage } from '@/lib/api/errors'
import { askQuestion, type AskAnswer, type AskStep } from './api'

export interface Exchange {
  id: string
  question: string
  status: 'running' | 'done' | 'error' | 'stopped'
  steps: AskStep[]
  answer?: AskAnswer
  error?: string
}

const STORAGE_KEY = 'salesapp.ask.history'
const KEEP = 10
export const INTERRUPTED = 'The page was reloaded before this answer arrived. Ask again to get it.'

/**
 * Questions and answers live here rather than in the page, so a question keeps running while the user looks at
 * other screens and its answer is waiting when they come back. Only Stop (or closing or reloading the tab) ends it.
 * The list is also kept in sessionStorage, so it survives a reload of the tab.
 */
function load(): Exchange[] {
  try {
    const saved = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '[]') as Exchange[]
    // A reload kills the request, so a question that was still running can't finish any more.
    return saved.map((e) => (e.status === 'running' ? { ...e, status: 'error', error: INTERRUPTED } : e))
  } catch {
    return []
  }
}

function save(list: Exchange[]) {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(list.slice(0, KEEP)))
  } catch {
    // Storage full or blocked: the history just won't survive a reload.
  }
}

let history: Exchange[] = load()
let pagesOpen = 0
const listeners = new Set<() => void>()
const controllers = new Map<string, AbortController>()

function set(next: Exchange[]) {
  history = next
  save(history)
  listeners.forEach((listener) => listener())
}

function update(id: string, change: (e: Exchange) => Exchange) {
  set(history.map((e) => (e.id === id ? change(e) : e)))
}

/** When the answer arrives while the user is on another screen, a toast offers to take them to it. */
function announce(id: string) {
  const exchange = history.find((e) => e.id === id)
  if (pagesOpen > 0 || !exchange || exchange.status === 'stopped') return
  const view = { label: 'View', onClick: () => void import('@/routes').then(({ router }) => router.navigate('/ask')) }
  if (exchange.status === 'done') toast.success('Your answer is ready', { description: exchange.question, action: view })
  else toast.error("Your question couldn't be answered", { description: exchange.question, action: view })
}

export const askStore = {
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },

  getSnapshot: () => history,

  isRunning: () => history.some((e) => e.status === 'running'),

  async ask(text: string) {
    const question = text.trim()
    if (!question || askStore.isRunning()) return
    const id = crypto.randomUUID()
    const controller = new AbortController()
    controllers.set(id, controller)
    set([{ id, question, status: 'running' as const, steps: [] }, ...history].slice(0, KEEP))

    try {
      await askQuestion(
        question,
        {
          onStep: (step) => update(id, (e) => ({ ...e, steps: [...e.steps, step] })),
          onAnswer: (answer) => update(id, (e) => ({ ...e, status: 'done', answer })),
          onError: (message) => update(id, (e) => ({ ...e, status: 'error', error: message })),
        },
        controller.signal,
      )
      // A stream that ends without an answer or error (connection dropped) shouldn't spin forever.
      update(id, (e) => (e.status === 'running' ? { ...e, status: 'error', error: 'The answer was cut off. Try asking again.' } : e))
    } catch (error) {
      update(id, (e) => (controller.signal.aborted ? { ...e, status: 'stopped' } : { ...e, status: 'error', error: errorMessage(error) }))
    } finally {
      controllers.delete(id)
      announce(id)
    }
  },

  /** The Stop button: cancels the request, so Claude stops working (and billing) on it. */
  stop() {
    controllers.forEach((controller) => controller.abort())
  },

  remove(id: string) {
    controllers.get(id)?.abort()
    set(history.filter((e) => e.id !== id))
  },

  /** The Ask page reports while it's on screen, so answers arriving elsewhere get a toast instead. */
  pageOpened() {
    pagesOpen++
    return () => {
      pagesOpen--
    }
  },
}

export function useAskHistory() {
  return useSyncExternalStore(askStore.subscribe, askStore.getSnapshot)
}
