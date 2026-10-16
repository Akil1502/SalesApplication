import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type CreditNote = Schemas['CreditNoteDto']
export type CreditNoteListItem = Schemas['CreditNoteListItemDto']
export type CreditNoteKind = Schemas['CreditNoteKind']
export type ReturnReason = Schemas['ReturnReason']
export type ReturnDisposition = NonNullable<Schemas['ReturnDisposition']>
export type ReturnDocumentStatus = Schemas['ReturnDocumentStatus']
export type CreditNoteCreateRequest = Schemas['CreditNoteCreateRequest']
export type CreditNotePreview = Schemas['CreditNotePreviewDto']
export type SupplierReturn = Schemas['SupplierReturnDto']
export type SupplierReturnListItem = Schemas['SupplierReturnListItemDto']
export type SupplierReturnCreateRequest = Schemas['SupplierReturnCreateRequest']
export type SupplierReturnPreview = Schemas['SupplierReturnPreviewDto']
export type AvailableCredit = Schemas['AvailableCreditDto']
export type CreditUse = Schemas['CreditUseDto']
export type Refund = Schemas['RefundDto']
export type ReturnSummary = Schemas['ReturnSummaryDto']
export type RefundCreateRequest = Schemas['RefundCreateRequest']

interface Paged {
  q?: string
  page: number
  pageSize: number
}

/** A return changes invoices or bills (balance, status), stock and cost, credit balances and every sales figure. */
function useInvalidateReturns() {
  const queryClient = useQueryClient()
  return () => {
    for (const key of ['credit-notes', 'supplier-returns', 'invoices', 'payments', 'supplier-bills', 'purchase-orders', 'stock', 'products', 'dashboard', 'reports']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
  }
}

// ---- Credit notes ----------------------------------------------------------------

export function useCreditNotes(
  query: Paged & { status?: ReturnDocumentStatus; kind?: CreditNoteKind; customerId?: number; invoiceId?: number; withCreditOnly?: boolean },
) {
  return useQuery({
    queryKey: ['credit-notes', 'list', query],
    queryFn: () => unwrap(api.GET('/api/credit-notes', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useCreditNoteSummary() {
  return useQuery({ queryKey: ['credit-notes', 'summary'], queryFn: () => unwrap(api.GET('/api/credit-notes/summary')) })
}

export function useCreditNote(id: number) {
  return useQuery({
    queryKey: ['credit-notes', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/credit-notes/{id}', { params: { path: { id } } })),
  })
}

/** A customer's credit notes with credit left, oldest first. */
export function useAvailableCredit(customerId: number, enabled: boolean) {
  return useQuery({
    queryKey: ['credit-notes', 'available', customerId],
    queryFn: () => unwrap(api.GET('/api/credit-notes/available', { params: { query: { customerId } } })),
    enabled,
  })
}

/** What a credit note would come to, from the server, so the figures match what saving produces. */
export function useCreditNotePreview(request: CreditNoteCreateRequest | null) {
  return useQuery({
    queryKey: ['credit-notes', 'preview', request],
    queryFn: () => unwrap(api.POST('/api/credit-notes/preview', { body: request! })),
    enabled: request !== null,
    retry: false,
    placeholderData: keepPreviousData,
  })
}

export function useCreateCreditNote() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: (body: CreditNoteCreateRequest) => unwrap(api.POST('/api/credit-notes', { body })),
    onSuccess: invalidate,
  })
}

export function useApplyCreditNote() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, ...body }: { id: number; documentId: number; amount: number; date: string; rowVersion: string }) =>
      unwrap(api.POST('/api/credit-notes/{id}/adjustments', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useRefundCreditNote() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: RefundCreateRequest }) =>
      unwrap(api.POST('/api/credit-notes/{id}/refunds', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useCancelCreditNote() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, reason, rowVersion }: { id: number; reason: string; rowVersion: string }) =>
      unwrap(api.POST('/api/credit-notes/{id}/cancel', { params: { path: { id } }, body: { reason, rowVersion } })),
    onSuccess: invalidate,
  })
}

// ---- Supplier returns --------------------------------------------------------------

export function useSupplierReturns(
  query: Paged & { status?: ReturnDocumentStatus; supplierId?: number; supplierBillId?: number; withCreditOnly?: boolean; awaitingSupplierNoteOnly?: boolean },
) {
  return useQuery({
    queryKey: ['supplier-returns', 'list', query],
    queryFn: () => unwrap(api.GET('/api/supplier-returns', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useSupplierReturnSummary() {
  return useQuery({ queryKey: ['supplier-returns', 'summary'], queryFn: () => unwrap(api.GET('/api/supplier-returns/summary')) })
}

export function useSupplierReturn(id: number) {
  return useQuery({
    queryKey: ['supplier-returns', 'detail', id],
    queryFn: () => unwrap(api.GET('/api/supplier-returns/{id}', { params: { path: { id } } })),
  })
}

export function useAvailableSupplierCredit(supplierId: number, enabled: boolean) {
  return useQuery({
    queryKey: ['supplier-returns', 'available', supplierId],
    queryFn: () => unwrap(api.GET('/api/supplier-returns/available', { params: { query: { supplierId } } })),
    enabled,
  })
}

export function useSupplierReturnPreview(request: SupplierReturnCreateRequest | null) {
  return useQuery({
    queryKey: ['supplier-returns', 'preview', request],
    queryFn: () => unwrap(api.POST('/api/supplier-returns/preview', { body: request! })),
    enabled: request !== null,
    retry: false,
    placeholderData: keepPreviousData,
  })
}

export function useCreateSupplierReturn() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: (body: SupplierReturnCreateRequest) => unwrap(api.POST('/api/supplier-returns', { body })),
    onSuccess: invalidate,
  })
}

export function useNoteSupplierCreditNote() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, ...body }: { id: number; supplierCreditNoteNumber: string; supplierCreditNoteDate: string | null; rowVersion: string }) =>
      unwrap(api.PUT('/api/supplier-returns/{id}/supplier-credit-note', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useApplySupplierCredit() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, ...body }: { id: number; documentId: number; amount: number; date: string; rowVersion: string }) =>
      unwrap(api.POST('/api/supplier-returns/{id}/adjustments', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useRefundSupplierReturn() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: RefundCreateRequest }) =>
      unwrap(api.POST('/api/supplier-returns/{id}/refunds', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useCancelSupplierReturn() {
  const invalidate = useInvalidateReturns()
  return useMutation({
    mutationFn: ({ id, reason, rowVersion }: { id: number; reason: string; rowVersion: string }) =>
      unwrap(api.POST('/api/supplier-returns/{id}/cancel', { params: { path: { id } }, body: { reason, rowVersion } })),
    onSuccess: invalidate,
  })
}
