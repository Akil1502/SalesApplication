import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type StockLevel = Schemas['StockLevelDto']
export type StockStatus = Schemas['StockStatus']
export type StockMovement = Schemas['StockMovementDto']
export type StockMovementType = Schemas['StockMovementType']
export type StockReceiptListItem = Schemas['StockReceiptListItemDto']
export type StockReceipt = Schemas['StockReceiptDto']
export type AdjustmentReason = NonNullable<Schemas['AdjustmentReason']>
export type AdjustmentMode = Schemas['AdjustmentMode']

export const movementTypeLabels: Record<StockMovementType, string> = {
  Opening: 'Opening',
  Receipt: 'Receipt',
  Adjustment: 'Adjustment',
  Sale: 'Sale',
  SaleReversal: 'Sale reversal',
}

export const adjustmentReasons: { value: AdjustmentReason; label: string }[] = [
  { value: 'StockCount', label: 'Stock count' },
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Lost', label: 'Lost / missing' },
  { value: 'Found', label: 'Found' },
  { value: 'OpeningBalance', label: 'Opening balance' },
  { value: 'Other', label: 'Other' },
]

export const reasonLabel = (reason: AdjustmentReason | null | undefined) =>
  adjustmentReasons.find((r) => r.value === reason)?.label ?? ''

export const stockKeys = {
  all: ['stock'] as const,
  summary: ['stock', 'summary'] as const,
}

interface Paged {
  q?: string
  page: number
  pageSize: number
}

export function useStockSummary() {
  return useQuery({ queryKey: stockKeys.summary, queryFn: () => unwrap(api.GET('/api/stock/summary')) })
}

export function useStockLevels(query: Paged & { sort?: string; categoryId?: number; status?: StockStatus; includeInactive?: boolean }) {
  return useQuery({
    queryKey: ['stock', 'levels', query],
    queryFn: () => unwrap(api.GET('/api/stock/levels', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useStockMovements(query: Paged & { productId?: number; type?: StockMovementType; from?: string; to?: string }) {
  return useQuery({
    queryKey: ['stock', 'movements', query],
    queryFn: () => unwrap(api.GET('/api/stock/movements', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useStockReceipts(query: Paged) {
  return useQuery({
    queryKey: ['stock', 'receipts', query],
    queryFn: () => unwrap(api.GET('/api/stock/receipts', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useStockReceipt(id: number) {
  return useQuery({
    queryKey: ['stock', 'receipt', id],
    queryFn: () => unwrap(api.GET('/api/stock/receipts/{id}', { params: { path: { id } } })),
  })
}

/** Stock changes affect stock screens and product lists (on-hand quantities, low-stock flags). */
function useInvalidateStock() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: stockKeys.all })
    void queryClient.invalidateQueries({ queryKey: ['products'] })
  }
}

export function useCreateReceipt() {
  const invalidate = useInvalidateStock()
  return useMutation({
    mutationFn: (body: Schemas['StockReceiptCreateRequest']) => unwrap(api.POST('/api/stock/receipts', { body })),
    onSuccess: invalidate,
  })
}

export function useAdjustStock() {
  const invalidate = useInvalidateStock()
  return useMutation({
    mutationFn: (body: Schemas['StockAdjustmentRequest']) => unwrap(api.POST('/api/stock/adjustments', { body })),
    onSuccess: invalidate,
  })
}
