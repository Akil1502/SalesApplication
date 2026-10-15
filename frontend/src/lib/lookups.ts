import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

/** Query keys for reference data, shared so mutations can invalidate them. */
export const lookupKeys = {
  states: ['lookups', 'states'] as const,
  salesReps: ['lookups', 'sales-reps'] as const,
  categories: (includeInactive: boolean) => ['categories', { includeInactive }] as const,
  hsnCodes: (includeInactive: boolean) => ['hsn-codes', { includeInactive }] as const,
}

/** States and union territories. Static reference data, so cached for the session. */
export function useStates() {
  return useQuery({
    queryKey: lookupKeys.states,
    queryFn: () => unwrap(api.GET('/api/lookups/states')),
    staleTime: Infinity,
  })
}

/** Users who can be assigned customers. Managers and admins only. */
export function useSalesReps(enabled: boolean) {
  return useQuery({
    queryKey: lookupKeys.salesReps,
    queryFn: () => unwrap(api.GET('/api/lookups/sales-reps')),
    enabled,
    staleTime: 5 * 60_000,
  })
}

export function useCategories(includeInactive = false) {
  return useQuery({
    queryKey: lookupKeys.categories(includeInactive),
    queryFn: () => unwrap(api.GET('/api/categories', { params: { query: { includeInactive } } })),
  })
}

export function useHsnCodes(includeInactive = false) {
  return useQuery({
    queryKey: lookupKeys.hsnCodes(includeInactive),
    queryFn: () => unwrap(api.GET('/api/hsn-codes', { params: { query: { includeInactive } } })),
  })
}
