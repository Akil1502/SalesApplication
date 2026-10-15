import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type Insights = Schemas['InsightsDto']
export type InsightMetrics = Schemas['InsightMetricsDto']
export type Anomaly = Schemas['AnomalyDto']
export type AnomalyKind = Schemas['AnomalyKind']
export type ForecastMethod = Schemas['ForecastMethod']
export type InsightCardKind = Schemas['InsightCardKind']
export type ReorderSuggestion = Schemas['ReorderSuggestionDto']
export type CustomerTrend = Schemas['CustomerTrendDto']

export const insightsQueryKey = ['insights'] as const

/** The latest insights, or null when they've never been computed (the API answers 204). */
export function useInsights() {
  return useQuery({
    queryKey: insightsQueryKey,
    queryFn: async (): Promise<Insights | null> => {
      const result = await api.GET('/api/insights')
      if (result.response.status === 204) return null
      return (await unwrap(Promise.resolve(result))) as Insights
    },
  })
}

/** Recomputes now: the figures take a few seconds, the written summary up to about twenty more. */
export function useRefreshInsights() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/insights/refresh')),
    onSuccess: (insights) => queryClient.setQueryData(insightsQueryKey, insights),
  })
}
