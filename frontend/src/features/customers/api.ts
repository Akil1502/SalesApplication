import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type CustomerListItem = Schemas['CustomerListItemDto']
export type Customer = Schemas['CustomerDto']
export type CustomerSaveRequest = Schemas['CustomerSaveRequest']

export interface CustomerListQuery {
  q?: string
  page: number
  pageSize: number
  sort?: string
  includeInactive?: boolean
  stateCode?: string
  assignedRepId?: string
}

export const customerKeys = {
  all: ['customers'] as const,
  list: (query: CustomerListQuery) => ['customers', 'list', query] as const,
  detail: (id: number) => ['customers', 'detail', id] as const,
}

export function useCustomers(query: CustomerListQuery) {
  return useQuery({
    queryKey: customerKeys.list(query),
    queryFn: () => unwrap(api.GET('/api/customers', { params: { query } })),
    // Keep showing the current page while the next one loads, instead of flashing a skeleton.
    placeholderData: keepPreviousData,
  })
}

export function useCustomer(id: number | undefined) {
  return useQuery({
    queryKey: customerKeys.detail(id ?? 0),
    queryFn: () => unwrap(api.GET('/api/customers/{id}', { params: { path: { id: id! } } })),
    enabled: id !== undefined,
  })
}

export function useSaveCustomer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number; body: CustomerSaveRequest }) =>
      id === undefined
        ? unwrap(api.POST('/api/customers', { body }))
        : unwrap(api.PUT('/api/customers/{id}', { params: { path: { id } }, body })),
    onSuccess: (customer) => {
      queryClient.setQueryData(customerKeys.detail(customer.id), customer)
      void queryClient.invalidateQueries({ queryKey: ['customers', 'list'] })
    },
  })
}
