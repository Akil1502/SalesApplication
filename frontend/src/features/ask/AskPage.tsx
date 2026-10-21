import { CheckCircle2, Database, MessageSquareText, RotateCcw, ShieldAlert, Sparkles, Square, TriangleAlert, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { StillOrb } from '@/components/fx/StillOrb'
import { PageHeader } from '@/components/PageHeader'
import { AiOrb3D } from '@/components/three/scenes'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { msg, t } from '@/lib/i18n'
import type { AskHandoff, AskStep } from './api'
import { AnswerCard } from './AnswerCard'
import { askStore, useAskHistory } from './ask-store'

// Asked in the language on screen: a Tamil user's examples are in Tamil.
const examples = [
  msg('Top 10 customers by sales this financial year'),
  msg('Monthly sales for the last 12 months'),
  msg('Which products are low or out of stock?'),
  msg('Who owes us the most, and how overdue is it?'),
  msg('Sales by state last financial year'),
]
const managerExamples = [msg('Gross margin by product category this year')]

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
  const { can } = useAuth()
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <MessageSquareText />
        </EmptyMedia>
        <EmptyTitle>{t('Ask your data is switched off')}</EmptyTitle>
        <EmptyDescription>
          {can(Permissions.SettingsEdit)
            ? [reason, t("Add a Claude API key to the server's user-secrets (see the README), then restart the API.")].filter(Boolean).join(' ')
            : t('Ask your administrator to set up the AI features. The reports have the standard figures.')}
        </EmptyDescription>
      </EmptyHeader>
      <Button asChild variant="outline">
        <Link to="/reports">{t('Open reports')}</Link>
      </Button>
    </Empty>
  )
}

export function AskPage() {
  const status = useAiStatus()
  const { can } = useAuth()
  const [question, setQuestion] = useState('')
  // The questions live in a store outside this page: leaving the page doesn't cancel a running one.
  const history = useAskHistory()
  const running = history.some((e) => e.status === 'running')
  const input = useRef<HTMLTextAreaElement>(null)

  // A question handed over from the assistant is filled in, not asked: asking costs money, so the user clicks Ask.
  const location = useLocation()
  const [handedOver, setHandedOver] = useState<string | null>(null)
  const handoff = (location.state as AskHandoff | null)?.question
  if (handoff && handedOver !== location.key) {
    setHandedOver(location.key)
    setQuestion(handoff)
  }

  useEffect(() => askStore.pageOpened(), [])
  useEffect(() => {
    if (handedOver) input.current?.focus()
  }, [handedOver])

  function ask(text: string) {
    if (!text.trim() || running) return
    setQuestion('')
    void askStore.ask(text)
  }

  if (status.isPending) return <Skeleton className="h-96 w-full rounded-xl" />

  return (
    <>
      <PageHeader
        title={t('Ask your data')}
        description={t('Ask about sales, customers, products, stock or payments in plain English or Tamil. Answers come straight from your sales records, and the SQL used is shown with each one.')}
      />

      {!status.data?.askData ? (
        <AiOff reason={status.data?.reason} />
      ) : (
        <>
          <Card className="relative overflow-hidden">
            <div
              aria-hidden
              className="pointer-events-none absolute inset-y-0 left-0 w-72 opacity-70"
              style={{ background: 'radial-gradient(circle at 30% 50%, color-mix(in oklch, var(--brand) 14%, transparent), transparent 70%)' }}
            />
            <CardContent className="relative flex flex-col gap-4 sm:flex-row sm:items-center">
              <AiOrb3D
                active={running}
                className="mx-auto size-32 shrink-0 sm:mx-0 sm:size-36"
                fallback={<StillOrb active={running} className="mx-auto size-32 shrink-0 sm:mx-0 sm:size-36" />}
              />
              <div className="flex min-w-0 flex-1 flex-col gap-3">
                <form
                  className="flex flex-col gap-3 sm:flex-row sm:items-end"
                  onSubmit={(e) => {
                    e.preventDefault()
                    ask(question)
                  }}
                >
                  <Textarea
                    ref={input}
                    aria-label={t('Your question')}
                    rows={2}
                    maxLength={500}
                    value={question}
                    onChange={(e) => setQuestion(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' && !e.shiftKey) {
                        e.preventDefault()
                        ask(question)
                      }
                    }}
                    placeholder={t('e.g. Which customers bought less this year than last year?')}
                    disabled={running}
                    className="min-h-16 flex-1"
                  />
                  {running ? (
                    <Button type="button" variant="outline" onClick={() => askStore.stop()}>
                      <Square /> {t('Stop')}
                    </Button>
                  ) : (
                    <Button type="submit" disabled={!question.trim()}>
                      <Sparkles /> {t('Ask')}
                    </Button>
                  )}
                </form>
                {history.length === 0 && (
                  <div className="flex flex-wrap gap-2">
                    {[...examples, ...(can(Permissions.CostPricesView) ? managerExamples : [])].map((example) => (
                      <Button key={example} size="sm" variant="outline" className="h-auto py-1 whitespace-normal" onClick={() => ask(t(example))}>
                        {t(example)}
                      </Button>
                    ))}
                  </div>
                )}
              </div>
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
                    aria-label={t('Remove this answer')}
                    onClick={() => askStore.remove(exchange.id)}
                  >
                    <X />
                  </Button>
                )}
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                {exchange.status === 'running' && (
                  <ul className="flex flex-col gap-2" aria-live="polite">
                    {exchange.steps.length === 0 && <StepLine step={{ kind: 'Thinking', message: t('Starting') }} current />}
                    {exchange.steps.map((step, i) => (
                      <StepLine key={i} step={step} current={i === exchange.steps.length - 1} />
                    ))}
                  </ul>
                )}
                {exchange.status === 'done' && exchange.answer && <AnswerCard answer={exchange.answer} onFollowUp={ask} />}
                {exchange.status === 'error' && (
                  <div className="flex flex-col items-start gap-3">
                    <Alert variant="destructive">
                      <AlertDescription>{exchange.error && t(exchange.error)}</AlertDescription>
                    </Alert>
                    <Button size="sm" variant="outline" disabled={running} onClick={() => ask(exchange.question)}>
                      <RotateCcw /> {t('Ask again')}
                    </Button>
                  </div>
                )}
                {exchange.status === 'stopped' && <p className="text-sm text-muted-foreground">{t('Stopped.')}</p>}
              </CardContent>
            </Card>
          ))}
        </>
      )}
    </>
  )
}
