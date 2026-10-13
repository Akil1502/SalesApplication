import { useQuery } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type AiStatus = Schemas['AiStatusDto']

/** Whether the AI features can be used. Screens and menu items for them are hidden while it's off. */
export function useAiStatus() {
  return useQuery({
    queryKey: ['ai', 'status'],
    queryFn: () => unwrap(api.GET('/api/ai/status')),
    staleTime: 5 * 60_000,
  })
}
