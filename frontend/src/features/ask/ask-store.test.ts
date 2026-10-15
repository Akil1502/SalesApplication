import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AskAnswer } from './api'

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }))
vi.mock('sonner', () => ({ toast }))

const askQuestion = vi.hoisted(() => vi.fn())
vi.mock('./api', () => ({ askQuestion }))

const answer = { summary: 'Murugan Garments is the top customer.' } as AskAnswer

/** The store keeps module state, so each test loads a fresh copy (after setting up sessionStorage). */
async function freshStore() {
  vi.resetModules()
  return import('./ask-store')
}

/** A request that waits until the test lets it finish, and fails like fetch does when it's aborted. */
function pendingAnswer() {
  let finish = () => {}
  askQuestion.mockImplementation(
    (_q: string, handlers: { onAnswer: (a: AskAnswer) => void }, signal: AbortSignal) =>
      new Promise<void>((resolve, reject) => {
        signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
        finish = () => {
          handlers.onAnswer(answer)
          resolve()
        }
      }),
  )
  return () => finish()
}

describe('ask store', () => {
  beforeEach(() => sessionStorage.clear())
  afterEach(() => vi.clearAllMocks())

  it('keeps a question running after the page closes, and says when the answer is ready', async () => {
    const { askStore } = await freshStore()
    const finish = pendingAnswer()
    const closePage = askStore.pageOpened()

    const asking = askStore.ask('Top 10 customers this year')
    expect(askStore.getSnapshot()[0]).toMatchObject({ question: 'Top 10 customers this year', status: 'running' })
    closePage() // the user goes to another screen
    finish()
    await asking

    expect(askStore.getSnapshot()[0]).toMatchObject({ status: 'done', answer })
    expect(toast.success).toHaveBeenCalledWith('Your answer is ready', expect.objectContaining({ description: 'Top 10 customers this year' }))
    expect(JSON.parse(sessionStorage.getItem('salesapp.ask.history')!)[0].status).toBe('done')
  })

  it("doesn't pop up a toast while the Ask page is open", async () => {
    const { askStore } = await freshStore()
    const finish = pendingAnswer()
    askStore.pageOpened()

    const asking = askStore.ask('Who owes us the most?')
    finish()
    await asking

    expect(askStore.getSnapshot()[0]?.status).toBe('done')
    expect(toast.success).not.toHaveBeenCalled()
  })

  it('Stop cancels the request', async () => {
    const { askStore } = await freshStore()
    pendingAnswer()

    const asking = askStore.ask('Monthly sales')
    askStore.stop()
    await asking

    expect(askStore.getSnapshot()[0]?.status).toBe('stopped')
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
  })

  it('asks one question at a time', async () => {
    const { askStore } = await freshStore()
    const finish = pendingAnswer()

    const asking = askStore.ask('First question')
    await askStore.ask('Second question')
    finish()
    await asking

    expect(askStore.getSnapshot().map((e) => e.question)).toEqual(['First question'])
  })

  it('after a reload, a question that was still running shows as interrupted', async () => {
    sessionStorage.setItem('salesapp.ask.history', JSON.stringify([{ id: '1', question: 'Sales by state', status: 'running', steps: [] }]))

    const { askStore, INTERRUPTED } = await freshStore()

    expect(askStore.getSnapshot()[0]).toMatchObject({ status: 'error', error: INTERRUPTED })
  })
})

describe('ask store on sign-out', () => {
  it("clears the history, so the next person signing in on this tab doesn't see it", async () => {
    sessionStorage.setItem('salesapp.ask.history', JSON.stringify([{ id: '1', question: 'Who owes us the most?', status: 'done', steps: [] }]))
    const { askStore } = await freshStore()
    const { authStore } = await import('@/features/auth/auth-store')
    authStore.set({ token: 't', expiresAtUtc: new Date(Date.now() + 3600e3).toISOString() })
    expect(askStore.getSnapshot()).toHaveLength(1)

    authStore.clear()

    expect(askStore.getSnapshot()).toEqual([])
    expect(JSON.parse(sessionStorage.getItem('salesapp.ask.history')!)).toEqual([])
  })
})
