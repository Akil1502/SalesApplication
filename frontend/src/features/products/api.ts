import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type ProductListItem = Schemas['ProductListItemDto']
export type Product = Schemas['ProductDto']
export type ProductSaveRequest = Schemas['ProductSaveRequest']
export type ProductUnit = Schemas['ProductUnit']

export const productUnits: { value: ProductUnit; label: string }[] = [
  { value: 'Kg', label: 'Kilogram (kg)' },
  { value: 'Metre', label: 'Metre (m)' },
  { value: 'Piece', label: 'Piece' },
  { value: 'Cone', label: 'Cone' },
  { value: 'Bale', label: 'Bale' },
  { value: 'Box', label: 'Box' },
  { value: 'Roll', label: 'Roll' },
  { value: 'Set', label: 'Set' },
]

export interface ProductListQuery {
  q?: string
  page: number
  pageSize: number
  sort?: string
  includeInactive?: boolean
  categoryId?: number
  lowStockOnly?: boolean
}

export const productKeys = {
  list: (query: ProductListQuery) => ['products', 'list', query] as const,
  detail: (id: number) => ['products', 'detail', id] as const,
}

export function useProducts(query: ProductListQuery) {
  return useQuery({
    queryKey: productKeys.list(query),
    queryFn: () => unwrap(api.GET('/api/products', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useProduct(id: number | undefined) {
  return useQuery({
    queryKey: productKeys.detail(id ?? 0),
    queryFn: () => unwrap(api.GET('/api/products/{id}', { params: { path: { id: id! } } })),
    enabled: id !== undefined,
  })
}

export function useSaveProduct() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number; body: ProductSaveRequest }) =>
      id === undefined
        ? unwrap(api.POST('/api/products', { body }))
        : unwrap(api.PUT('/api/products/{id}', { params: { path: { id } }, body })),
    onSuccess: (product) => {
      queryClient.setQueryData(productKeys.detail(product.id), product)
      void queryClient.invalidateQueries({ queryKey: ['products', 'list'] })
      // Product counts on categories and HSN codes change too.
      void queryClient.invalidateQueries({ queryKey: ['categories'] })
      void queryClient.invalidateQueries({ queryKey: ['hsn-codes'] })
    },
  })
}
