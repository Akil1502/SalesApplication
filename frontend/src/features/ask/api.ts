import type { Schemas } from '@/lib/api/client'
import { postEventStream } from '@/lib/sse'

// The answer arrives inside a server-sent event stream, which OpenAPI can't describe, so these mirror the C#
// records in SalesApp.Core/Contracts/Ai/AiDtos.cs (AskStepDto, AskAnswerDto and friends).
export type AskColumnKind = 'Text' | 'Money' | 'Count' | 'Number' | 'Date' | 'Boolean'
export type AskStepKind = 'Thinking' | 'Query' | 'Rows' | 'Rejected' | 'Failed'
export type AskChartType = 'None' | 'Bar' | 'Line'
export type AskValue = string | number | boolean | null

export interface AskColumn {
  /** The SQL alias; the chart refers to columns by this. */
  name: string
  /** The header to show ("Sales value"). Missing on answers saved before labels existed. */
  label?: string
  kind: AskColumnKind
}

export interface AskStep {
  kind: AskStepKind
  message: string
  sql?: string | null
  rowCount?: number | null
}

export interface AskAnswer {
  runId: string
  question: string
  summary: string
  resultId?: string | null
  sql?: string | null
  purpose?: string | null
  columns: AskColumn[]
  rows: AskValue[][]
  truncated: boolean
  chart: { type: AskChartType; x?: string | null; y?: string | null }
  followUps: string[]
  queriesRun: number
  usage: Schemas['AiUsageDto']
}

export interface AskHandlers {
  onStep: (step: AskStep) => void
  onAnswer: (answer: AskAnswer) => void
  onError: (message: string) => void
}

/** Asks a question and reports progress as it streams. Abort the signal to stop (and stop paying for) the answer. */
export async function askQuestion(question: string, handlers: AskHandlers, signal?: AbortSignal): Promise<void> {
  await postEventStream(
    '/api/ask',
    { question },
    ({ event, data }) => {
      const payload: unknown = JSON.parse(data)
      if (event === 'step') handlers.onStep(payload as AskStep)
      else if (event === 'answer') handlers.onAnswer(payload as AskAnswer)
      else if (event === 'error') handlers.onError((payload as { message: string }).message)
    },
    signal,
  )
}
