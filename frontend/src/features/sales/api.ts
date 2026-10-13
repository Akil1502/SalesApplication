import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type Order = Schemas['OrderDto']
export type OrderLine = Schemas['OrderLineDto']
export type OrderListItem = Schemas['OrderListItemDto']
export type OrderStatus = Schemas['SalesOrderStatus']
export type OrderSaveRequest = Schemas['OrderSaveRequest']
export type Invoice = Schemas['InvoiceDto']
export type InvoiceListItem = Schemas['InvoiceListItemDto']
export type InvoiceStatus = Schemas['InvoiceStatus']
export type Payment = Schemas['PaymentDto']
export type PaymentMode = Schemas['PaymentMode']

interface Paged {
  q?: string
  page: number
  pageSize: number
}

// ---- Orders ----------------------------------------------------------------------

export function useOrders(query: Paged & { status?: OrderStatus; customerId?: number; from?: string; to?: string }) {
  return useQuery({
    queryKey: ['orders', 'list', query],
    queryFn: () => unwrap(api.GET('/api/orders', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useOrder(id: number | undefined) {
  return useQuery({
    queryKey: ['orders', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/orders/{id}', { params: { path: { id: id! } } })),
    enabled: id !== undefined,
  })
}

/** After any order change: refresh lists, and put the returned order straight into the cache. */
function useOrderMutation<TArgs>(mutationFn: (args: TArgs) => Promise<Order>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: (order) => {
      queryClient.setQueryData(['orders', 'detail', order.id], order)
      void queryClient.invalidateQueries({ queryKey: ['orders', 'list'] })
    },
  })
}

export function useSaveOrder() {
  return useOrderMutation(({ id, body }: { id?: number; body: OrderSaveRequest }) =>
    id === undefined
      ? unwrap(api.POST('/api/orders', { body }))
      : unwrap(api.PUT('/api/orders/{id}', { params: { path: { id } }, body })),
  )
}

export function useQuoteOrder() {
  return useOrderMutation(({ id, rowVersion }: { id: number; rowVersion: string }) =>
    unwrap(api.POST('/api/orders/{id}/quote', { params: { path: { id } }, body: { rowVersion } })),
  )
}

export function useConfirmOrder() {
  return useOrderMutation(({ id, rowVersion, overrideCreditLimit }: { id: number; rowVersion: string; overrideCreditLimit: boolean }) =>
    unwrap(api.POST('/api/orders/{id}/confirm', { params: { path: { id } }, body: { rowVersion, overrideCreditLimit } })),
  )
}

export function useCancelOrder() {
  return useOrderMutation(({ id, rowVersion, reason }: { id: number; rowVersion: string; reason: string }) =>
    unwrap(api.POST('/api/orders/{id}/cancel', { params: { path: { id } }, body: { rowVersion, reason } })),
  )
}

// ---- Invoices --------------------------------------------------------------------

export function useInvoices(query: Paged & { status?: InvoiceStatus; customerId?: number; from?: string; to?: string; overdueOnly?: boolean }) {
  return useQuery({
    queryKey: ['invoices', 'list', query],
    queryFn: () => unwrap(api.GET('/api/invoices', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useInvoiceSummary() {
  return useQuery({ queryKey: ['invoices', 'summary'], queryFn: () => unwrap(api.GET('/api/invoices/summary')) })
}

export function useInvoice(id: number) {
  return useQuery({
    queryKey: ['invoices', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/invoices/{id}', { params: { path: { id } } })),
  })
}

/** Invoices touch orders (status), stock (sales) and receivables, so refresh all of them. */
function useInvalidateSales() {
  const queryClient = useQueryClient()
  return () => {
    for (const key of ['invoices', 'orders', 'payments', 'stock', 'products']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
  }
}

export function useCreateInvoice() {
  const invalidate = useInvalidateSales()
  return useMutation({
    mutationFn: (body: Schemas['InvoiceCreateRequest']) => unwrap(api.POST('/api/invoices', { body })),
    onSuccess: invalidate,
  })
}

export function useCancelInvoice() {
  const invalidate = useInvalidateSales()
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) =>
      unwrap(api.POST('/api/invoices/{id}/cancel', { params: { path: { id } }, body: { reason } })),
    onSuccess: invalidate,
  })
}

// ---- Payments --------------------------------------------------------------------

export function usePayments(query: Paged & { mode?: PaymentMode; customerId?: number; from?: string; to?: string }) {
  return useQuery({
    queryKey: ['payments', 'list', query],
    queryFn: () => unwrap(api.GET('/api/payments', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useRecordPayment() {
  const invalidate = useInvalidateSales()
  return useMutation({
    mutationFn: (body: Schemas['PaymentCreateRequest']) => unwrap(api.POST('/api/payments', { body })),
    onSuccess: invalidate,
  })
}
