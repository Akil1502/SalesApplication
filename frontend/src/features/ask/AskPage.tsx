import { CheckCircle2, Database, MessageSquareText, ShieldAlert, Sparkles, Square, TriangleAlert, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import { useAiStatus } from '@/features/ai/api'
import { Roles, useAuth } from '@/features/auth/auth-context'
import { errorMessage } from '@/lib/api/errors'
import { askQuestion, type AskAnswer, type AskStep } from './api'
import { AnswerCard } from './AnswerCard'

interface Exchange {
  id: string
  question: string
  status: 'running' | 'done' | 'error' | 'stopped'
  steps: AskStep[]
  answer?: AskAnswer
  error?: string
}

const STORAGE_KEY = 'salesapp.ask.history'
const KEEP = 10

const examples = [
  'Top 10 customers by sales this financial year',
  'Monthly sales for the last 12 months',
  'Which products are low or out of stock?',
  'Who owes us the most, and how overdue is it?',
  'Sales by state last financial year',
]
const managerExamples = ['Gross margin by product category this year']

/** Answers from this browser session, so leaving the page and coming back doesn't lose them. */
function loadHistory(): Exchange[] {
  try {
    const saved = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '[]') as Exchange[]
    return saved.filter((e) => e.status !== 'running')
  } catch {
    return []
  }
}

function saveHistory(history: Exchange[]) {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(history.filter((e) => e.status !== 'running').slice(0, KEEP)))
  } catch {
    // Storage full or blocked: the history just won't survive a reload.
  }
}

function StepLine({ step, current }: { step: AskStep; current: boolean }) {
  const icon = {
    Thinking: current ? <Spinner className="size-4" /> : <Sparkles className="size-4 text-muted-foreground" />,
    Query: current ? <Spinner className="size-4" /> : <Database className="size-4 text-muted-foreground" />,
    Rows: <CheckCircle2 className="size-4 text-viz-up" />,
    Rejected: <ShieldAlert className="size-4 text-amber-600" />,
    Failed: <TriangleAlert className="size-4 text-amber-600" />,
  }[step.kind]
  return (
    <li className="flex items-start gap-2 text-sm">
      <span className="mt-0.5 shrink-0">{icon}</span>
      <span className={step.kind === 'Rejected' || step.kind === 'Failed' ? 'text-amber-800 dark:text-amber-300' : undefined}>{step.message}</span>
    </li>
  )
}

function AiOff({ reason }: { reason?: string | null }) {
  const { hasRole } = useAuth()
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <MessageSquareText />
        </EmptyMedia>
        <EmptyTitle>Ask your data is switched off</EmptyTitle>
        <EmptyDescription>
          {hasRole(Roles.Admin)
            ? `${reason ?? ''} Add a Claude API key to the server's user-secrets (see the README), then restart the API.`
            : 'Ask your administrator to set up the AI features. The reports have the standard figures.'}
        </EmptyDescription>
      </EmptyHeader>
      <Button asChild variant="outline">
        <Link to="/reports">Open reports</Link>
      </Button>
    </Empty>
  )
}

export function AskPage() {
  const status = useAiStatus()
  const { hasRole } = useAuth()
  const [question, setQuestion] = useState('')
  const [history, setHistory] = useState<Exchange[]>(loadHistory)
  const abort = useRef<AbortController | null>(null)
  const running = history.some((e) => e.status === 'running')

  useEffect(() => saveHistory(history), [history])
  useEffect(() => () => abort.current?.abort(), [])

  const update = (id: string, change: (e: Exchange) => Exchange) => setHistory((all) => all.map((e) => (e.id === id ? change(e) : e)))

  async function ask(text: string) {
    const trimmed = text.trim()
    if (!trimmed || running) return
    const id = crypto.randomUUID()
    const controller = new AbortController()
    abort.current = controller
    setQuestion('')
    setHistory((all) => [{ id, question: trimmed, status: 'running' as const, steps: [] }, ...all].slice(0, KEEP))

    try {
      await askQuestion(
        trimmed,
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
    }
  }

  if (status.isPending) return <Skeleton className="h-96 w-full rounded-xl" />

  return (
    <>
      <PageHeader
        title="Ask your data"
        description="Ask about sales, customers, products, stock or payments in plain English or Tamil. Answers come straight from your sales records, and the SQL used is shown with each one."
      />

      {!status.data?.askData ? (
        <AiOff reason={status.data?.reason} />
      ) : (
        <>
          <Card>
            <CardContent className="flex flex-col gap-3">
              <form
                className="flex flex-col gap-3 sm:flex-row sm:items-end"
                onSubmit={(e) => {
                  e.preventDefault()
                  void ask(question)
                }}
              >
                <Textarea
                  aria-label="Your question"
                  rows={2}
                  maxLength={500}
                  value={question}
                  onChange={(e) => setQuestion(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault()
                      void ask(question)
                    }
                  }}
                  placeholder="e.g. Which customers bought less this year than last year?"
                  disabled={running}
                  className="min-h-16 flex-1"
                />
                {running ? (
                  <Button type="button" variant="outline" onClick={() => abort.current?.abort()}>
                    <Square /> Stop
                  </Button>
                ) : (
                  <Button type="submit" disabled={!question.trim()}>
                    <Sparkles /> Ask
                  </Button>
                )}
              </form>
              {history.length === 0 && (
                <div className="flex flex-wrap gap-2">
                  {[...examples, ...(hasRole(Roles.Admin, Roles.SalesManager) ? managerExamples : [])].map((example) => (
                    <Button key={example} size="sm" variant="outline" className="h-auto py-1 whitespace-normal" onClick={() => void ask(example)}>
                      {example}
                    </Button>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>

          {history.map((exchange) => (
            <Card key={exchange.id}>
              <CardHeader className="flex flex-row items-start justify-between gap-3">
                <CardTitle className="text-base leading-snug">{exchange.question}</CardTitle>
                {exchange.status !== 'running' && (
                  <Button
                    variant="ghost"
                    size="icon"
                    className="-mt-1 shrink-0"
                    aria-label="Remove this answer"
                    onClick={() => setHistory((all) => all.filter((e) => e.id !== exchange.id))}
                  >
                    <X />
                  </Button>
                )}
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                {exchange.status === 'running' && (
                  <ul className="flex flex-col gap-2" aria-live="polite">
                    {exchange.steps.length === 0 && <StepLine step={{ kind: 'Thinking', message: 'Starting' }} current />}
                    {exchange.steps.map((step, i) => (
                      <StepLine key={i} step={step} current={i === exchange.steps.length - 1} />
                    ))}
                  </ul>
                )}
                {exchange.status === 'done' && exchange.answer && <AnswerCard answer={exchange.answer} onFollowUp={(q) => void ask(q)} />}
                {exchange.status === 'error' && (
                  <Alert variant="destructive">
                    <AlertDescription>{exchange.error}</AlertDescription>
                  </Alert>
                )}
                {exchange.status === 'stopped' && <p className="text-sm text-muted-foreground">Stopped.</p>}
              </CardContent>
            </Card>
          ))}
        </>
      )}
    </>
  )
}
