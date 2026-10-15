import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type Category = Schemas['CategoryDto']
export type CategorySaveRequest = Schemas['CategorySaveRequest']
export type HsnCode = Schemas['HsnCodeDto']
export type HsnCodeSaveRequest = Schemas['HsnCodeSaveRequest']

export function useSaveCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number; body: CategorySaveRequest }) =>
      id === undefined
        ? unwrap(api.POST('/api/categories', { body }))
        : unwrap(api.PUT('/api/categories/{id}', { params: { path: { id } }, body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories'] }),
  })
}

export function useDeleteCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/categories/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories'] }),
  })
}

export function useSaveHsnCode() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number; body: HsnCodeSaveRequest }) =>
      id === undefined
        ? unwrap(api.POST('/api/hsn-codes', { body }))
        : unwrap(api.PUT('/api/hsn-codes/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['hsn-codes'] })
      // Products show their HSN code and GST rate.
      void queryClient.invalidateQueries({ queryKey: ['products'] })
    },
  })
}

export function useDeleteHsnCode() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/hsn-codes/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hsn-codes'] }),
  })
}
