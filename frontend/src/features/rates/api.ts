import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { ApiRequestError, isApiError, unwrap } from '@/lib/api/errors'
import { t } from '@/lib/i18n'

export type ProductRate = Schemas['ProductRateDto']
export type GridProduct = Schemas['ProductListItemDto']
export type RateImportResult = Schemas['RateImportResult']

export interface RateListQuery {
  q?: string
  page: number
  pageSize: number
  productId?: number
  activeOn?: string
}

export function useRates(query: RateListQuery, enabled = true) {
  return useQuery({
    queryKey: ['rates', 'list', query],
    queryFn: () => unwrap(api.GET('/api/rates', { params: { query } })),
    placeholderData: keepPreviousData,
    enabled,
  })
}

/**
 * Every active product matching the filter, priced on `priceOn`, for the rate grid. The API pages at 100, so this
 * reads page after page: the grid and "apply % to all" need all of them, not just one page.
 */
export function useGridProducts(priceOn: string, categoryId: number | undefined, q: string) {
  return useQuery({
    queryKey: ['rates', 'grid', priceOn, categoryId, q],
    enabled: /^\d{4}-\d{2}-\d{2}$/.test(priceOn),
    queryFn: async () => {
      const all: GridProduct[] = []
      for (let page = 1; ; page++) {
        const result = await unwrap(
          api.GET('/api/products', { params: { query: { page, pageSize: 100, sort: 'name', priceOn, categoryId, q: q || undefined } } }),
        )
        all.push(...result.items)
        if (page >= (result.totalPages ?? 1) || page >= 100) return all
      }
    },
    placeholderData: keepPreviousData,
  })
}

function useInvalidateRates() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['rates'] })
    // Product prices shown elsewhere (lists, the order form's picker) may have changed.
    void queryClient.invalidateQueries({ queryKey: ['products'] })
  }
}

export function useSaveRates() {
  const invalidate = useInvalidateRates()
  return useMutation({
    mutationFn: (body: Schemas['RateBatchRequest']) => unwrap(api.POST('/api/rates/batch', { body })),
    onSuccess: invalidate,
  })
}

export function useUpdateRate() {
  const invalidate = useInvalidateRates()
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Schemas['RateUpdateRequest'] }) =>
      unwrap(api.PUT('/api/rates/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useDeleteRate() {
  const invalidate = useInvalidateRates()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/rates/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  })
}

/** Reads a filled-in rate sheet on the server. Nothing is saved; the rows come back to review. */
export function useImportRates() {
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file, file.name)
      // The browser sets the multipart boundary itself when the body is FormData.
      return unwrap(api.POST('/api/rates/import', { body: {} as never, bodySerializer: () => form }))
    },
  })
}

/** Downloads the rate sheet with the signed-in user's token (a plain link can't send it) and saves it. */
export async function downloadRateSheet(query: { on: string; categoryId?: number; q?: string }): Promise<void> {
  const { data, error, response } = await api.GET('/api/rates/excel', { params: { query }, parseAs: 'blob' })
  if (!response.ok || !data) {
    throw new ApiRequestError(response.status, isApiError(error) ? error : { message: t('The rate sheet could not be created.'), traceId: '' })
  }

  const fileName = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? `rates-${query.on}.xlsx`
  const url = URL.createObjectURL(data as Blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  // Revoking straight away can cancel the download in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
