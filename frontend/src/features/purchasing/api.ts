import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type SupplierListItem = Schemas['SupplierListItemDto']
export type Supplier = Schemas['SupplierDto']
export type SupplierSaveRequest = Schemas['SupplierSaveRequest']
export type PurchaseOrder = Schemas['PurchaseOrderDto']
export type PurchaseOrderLine = Schemas['PurchaseOrderLineDto']
export type PurchaseOrderListItem = Schemas['PurchaseOrderListItemDto']
export type PurchaseOrderStatus = Schemas['PurchaseOrderStatus']
export type PurchaseOrderSaveRequest = Schemas['PurchaseOrderSaveRequest']
export type ReorderCandidate = Schemas['ReorderCandidateDto']
export type SupplierBill = Schemas['SupplierBillDto']
export type SupplierBillListItem = Schemas['SupplierBillListItemDto']
export type SupplierBillStatus = Schemas['SupplierBillStatus']
export type BillMatchStatus = Schemas['BillMatchStatus']
export type SupplierBillCreateRequest = Schemas['SupplierBillCreateRequest']
export type BillExtraction = Schemas['BillExtractionDto']
export type SupplierPayment = Schemas['SupplierPaymentDto']

interface Paged {
  q?: string
  page: number
  pageSize: number
}

// ---- Suppliers -------------------------------------------------------------------

export function useSuppliers(query: Paged & { sort?: string; includeInactive?: boolean; stateCode?: string }) {
  return useQuery({
    queryKey: ['suppliers', 'list', query],
    queryFn: () => unwrap(api.GET('/api/suppliers', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useSupplier(id: number | undefined) {
  return useQuery({
    queryKey: ['suppliers', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/suppliers/{id}', { params: { path: { id: id! } } })),
    enabled: id !== undefined,
  })
}

export function useSaveSupplier() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: number; body: SupplierSaveRequest }) =>
      id === undefined
        ? unwrap(api.POST('/api/suppliers', { body }))
        : unwrap(api.PUT('/api/suppliers/{id}', { params: { path: { id } }, body })),
    onSuccess: (supplier) => {
      queryClient.setQueryData(['suppliers', 'detail', supplier.id], supplier)
      void queryClient.invalidateQueries({ queryKey: ['suppliers', 'list'] })
    },
  })
}

// ---- Purchase orders -------------------------------------------------------------

export function usePurchaseOrders(
  query: Paged & { status?: PurchaseOrderStatus; supplierId?: number; openOnly?: boolean; lateOnly?: boolean },
) {
  return useQuery({
    queryKey: ['purchase-orders', 'list', query],
    queryFn: () => unwrap(api.GET('/api/purchase-orders', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function usePurchaseOrder(id: number | undefined) {
  return useQuery({
    queryKey: ['purchase-orders', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/purchase-orders/{id}', { params: { path: { id: id! } } })),
    enabled: id !== undefined,
  })
}

/** After any order change: put the returned order in the cache and refresh lists and supplier balances. */
function usePurchaseOrderMutation<TArgs>(mutationFn: (args: TArgs) => Promise<PurchaseOrder>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: (order) => {
      queryClient.setQueryData(['purchase-orders', 'detail', order.id], order)
      void queryClient.invalidateQueries({ queryKey: ['purchase-orders', 'list'] })
      void queryClient.invalidateQueries({ queryKey: ['purchase-orders', 'reorder'] })
      void queryClient.invalidateQueries({ queryKey: ['suppliers'] })
    },
  })
}

export function useSavePurchaseOrder() {
  return usePurchaseOrderMutation(({ id, body }: { id?: number; body: PurchaseOrderSaveRequest }) =>
    id === undefined
      ? unwrap(api.POST('/api/purchase-orders', { body }))
      : unwrap(api.PUT('/api/purchase-orders/{id}', { params: { path: { id } }, body })),
  )
}

export function usePlacePurchaseOrder() {
  return usePurchaseOrderMutation(({ id, rowVersion }: { id: number; rowVersion: string }) =>
    unwrap(api.POST('/api/purchase-orders/{id}/place', { params: { path: { id } }, body: { rowVersion } })),
  )
}

export function useApprovePurchaseOrder() {
  return usePurchaseOrderMutation(({ id, rowVersion }: { id: number; rowVersion: string }) =>
    unwrap(api.POST('/api/purchase-orders/{id}/approve', { params: { path: { id } }, body: { rowVersion } })),
  )
}

type ReasonArgs = { id: number; rowVersion: string; reason: string }

export function useRejectPurchaseOrder() {
  return usePurchaseOrderMutation(({ id, rowVersion, reason }: ReasonArgs) =>
    unwrap(api.POST('/api/purchase-orders/{id}/reject', { params: { path: { id } }, body: { rowVersion, reason } })),
  )
}

export function useCancelPurchaseOrder() {
  return usePurchaseOrderMutation(({ id, rowVersion, reason }: ReasonArgs) =>
    unwrap(api.POST('/api/purchase-orders/{id}/cancel', { params: { path: { id } }, body: { rowVersion, reason } })),
  )
}

export function useClosePurchaseOrder() {
  return usePurchaseOrderMutation(({ id, rowVersion, reason }: ReasonArgs) =>
    unwrap(api.POST('/api/purchase-orders/{id}/close', { params: { path: { id } }, body: { rowVersion, reason } })),
  )
}

/** Receiving changes the order, stock, product costs and the receipts list. */
export function useReceiveGoods() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Schemas['GoodsReceiptCreateRequest'] }) =>
      unwrap(api.POST('/api/purchase-orders/{id}/receipts', { params: { path: { id } }, body })),
    onSuccess: () => {
      for (const key of ['purchase-orders', 'stock', 'products', 'suppliers']) {
        void queryClient.invalidateQueries({ queryKey: [key] })
      }
    },
  })
}

/** What to reorder: products at or below their reorder level, or exactly the given products (AI insights' suggestions). */
export function useReorderCandidates(productIds: number[] | undefined, enabled = true) {
  return useQuery({
    queryKey: ['purchase-orders', 'reorder', productIds ?? null],
    queryFn: () => unwrap(api.GET('/api/purchase-orders/reorder-candidates', { params: { query: { productIds } } })),
    enabled,
  })
}

export function useCreateReorderDrafts() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['ReorderDraftRequest']) => unwrap(api.POST('/api/purchase-orders/reorder-drafts', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['purchase-orders'] }),
  })
}

// ---- Supplier bills --------------------------------------------------------------

export function useSupplierBills(
  query: Paged & { status?: SupplierBillStatus; matchStatus?: BillMatchStatus; supplierId?: number; purchaseOrderId?: number; overdueOnly?: boolean; unpaidOnly?: boolean },
) {
  return useQuery({
    queryKey: ['supplier-bills', 'list', query],
    queryFn: () => unwrap(api.GET('/api/supplier-bills', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useSupplierBillSummary() {
  return useQuery({ queryKey: ['supplier-bills', 'summary'], queryFn: () => unwrap(api.GET('/api/supplier-bills/summary')) })
}

export function useSupplierBill(id: number) {
  return useQuery({
    queryKey: ['supplier-bills', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/supplier-bills/{id}', { params: { path: { id } } })),
  })
}

/** Bills touch their order (billed quantities), product costs and payables, so refresh all of them. */
function useInvalidatePurchasing() {
  const queryClient = useQueryClient()
  return () => {
    for (const key of ['supplier-bills', 'supplier-payments', 'purchase-orders', 'suppliers', 'products']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
  }
}

export function useCreateSupplierBill() {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: (body: SupplierBillCreateRequest) => unwrap(api.POST('/api/supplier-bills', { body })),
    onSuccess: invalidate,
  })
}

export function useApproveSupplierBill() {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: ({ id, rowVersion, note }: { id: number; rowVersion: string; note: string }) =>
      unwrap(api.POST('/api/supplier-bills/{id}/approve', { params: { path: { id } }, body: { rowVersion, note } })),
    onSuccess: invalidate,
  })
}

export function useCancelSupplierBill() {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) =>
      unwrap(api.POST('/api/supplier-bills/{id}/cancel', { params: { path: { id } }, body: { reason } })),
    onSuccess: invalidate,
  })
}

/** Reads a supplier's bill (PDF or photo) with AI, against the order it's for. Nothing is saved. Takes 10 to 30 seconds. */
export function useReadBill() {
  return useMutation({
    mutationFn: ({ purchaseOrderId, file }: { purchaseOrderId: number; file: File }) => {
      const form = new FormData()
      form.append('purchaseOrderId', String(purchaseOrderId))
      form.append('file', file, file.name)
      // The browser sets the multipart boundary itself when the body is FormData.
      return unwrap(api.POST('/api/supplier-bills/read', { body: {} as never, bodySerializer: () => form }))
    },
  })
}

// ---- Supplier payments -----------------------------------------------------------

export function useSupplierPayments(query: Paged & { supplierId?: number; mode?: Schemas['PaymentMode'] }) {
  return useQuery({
    queryKey: ['supplier-payments', 'list', query],
    queryFn: () => unwrap(api.GET('/api/supplier-payments', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useRecordSupplierPayment() {
  const invalidate = useInvalidatePurchasing()
  return useMutation({
    mutationFn: (body: Schemas['SupplierPaymentCreateRequest']) => unwrap(api.POST('/api/supplier-payments', { body })),
    onSuccess: invalidate,
  })
}
