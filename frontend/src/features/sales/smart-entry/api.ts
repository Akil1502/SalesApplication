import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'

export type OrderExtraction = Schemas['OrderExtractionDto']
export type ExtractedLine = Schemas['ExtractedLineDto']
export type ExtractionConfidence = Schemas['ExtractionConfidence']
export type ProductMatchSource = Schemas['ProductMatchSource']
export type CustomerMatchSource = Schemas['CustomerMatchSource']
export type SmartOrderConfirmRequest = Schemas['SmartOrderConfirmRequest']

export interface ExtractInput {
  text?: string
  file?: File
  customerId?: number
}

/** Asks the server to read the order with Claude. Nothing is saved. Usually takes 10 to 30 seconds. */
export function useExtractOrder() {
  return useMutation({
    mutationFn: ({ text, file, customerId }: ExtractInput) => {
      const form = new FormData()
      if (text?.trim()) form.append('text', text)
      if (file) form.append('file', file, file.name)
      if (customerId) form.append('customerId', String(customerId))
      // The browser sets the multipart boundary itself when the body is FormData.
      return unwrap(api.POST('/api/orders/smart-entry/extract', { body: {} as never, bodySerializer: () => form }))
    },
  })
}

export function useConfirmSmartOrder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SmartOrderConfirmRequest) => unwrap(api.POST('/api/orders/smart-entry', { body: request })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['orders'] })
    },
  })
}
