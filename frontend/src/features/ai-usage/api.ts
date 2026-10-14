import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type AiUsageSummary = Schemas['AiUsageSummaryDto']
export type AiCall = Schemas['AiCallDto']
export type AiQuery = Schemas['AiQueryDto']
export type AiQueryOutcome = Schemas['AiQueryOutcome']

export function useAiUsageSummary(days: number) {
  return useQuery({
    queryKey: ['ai-usage', 'summary', days],
    queryFn: () => unwrap(api.GET('/api/admin/ai-usage/summary', { params: { query: { days } } })),
  })
}

export function useAiCalls(params: { page: number; feature?: string; problemsOnly: boolean }) {
  return useQuery({
    queryKey: ['ai-usage', 'calls', params],
    queryFn: () => unwrap(api.GET('/api/admin/ai-usage/calls', { params: { query: { ...params, pageSize: 20 } } })),
    placeholderData: keepPreviousData,
  })
}

export function useAiQueries(params: { page: number; q?: string; outcome?: AiQueryOutcome }) {
  return useQuery({
    queryKey: ['ai-usage', 'queries', params],
    queryFn: () => unwrap(api.GET('/api/admin/ai-usage/queries', { params: { query: { ...params, pageSize: 20 } } })),
    placeholderData: keepPreviousData,
  })
}

/** The names people know the features by. */
export const featureLabels: Record<string, string> = {
  OrderEntry: 'Smart order entry',
  AskData: 'Ask your data',
  Assistant: 'Assistant',
  Insights: 'AI insights',
}

export const featureLabel = (feature: string) => featureLabels[feature] ?? feature
