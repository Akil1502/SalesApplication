import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { msg } from '@/lib/i18n'
import type { components } from '@/lib/api/schema'
import { postEventStream } from '@/lib/sse'

type Schemas = components['schemas']

export type ConversationSummary = Schemas['AssistantConversationSummaryDto']
export type AssistantTool = Schemas['AssistantToolDto']
export type AssistantDraft = Schemas['AssistantDraftDto']
export type AssistantItemKind = Schemas['AssistantItemKind']
export type AiUsage = Schemas['AiUsageDto']

/** One entry of the conversation. The API leaves out empty fields, so they are filled in here. */
export interface AssistantItem {
  kind: AssistantItemKind
  text: string | null
  tool: AssistantTool | null
  draft: AssistantDraft | null
  interrupted: boolean
}

export interface Conversation {
  id: string
  title: string
  items: AssistantItem[]
  notice: string | null
  costInr: number
}

export const conversationsQueryKey = ['assistant', 'conversations'] as const

export function useConversations(enabled: boolean) {
  return useQuery({
    queryKey: conversationsQueryKey,
    queryFn: () => unwrap(api.GET('/api/assistant/conversations')),
    enabled,
    staleTime: 0,
  })
}

export function toTool(tool: AssistantTool): AssistantTool {
  return { ...tool, succeeded: tool.succeeded ?? null, detail: tool.detail ?? null }
}

export function toDraft(draft: AssistantDraft): AssistantDraft {
  return {
    ...draft,
    contactPerson: draft.contactPerson ?? null,
    email: draft.email ?? null,
    phone: draft.phone ?? null,
    subject: draft.subject ?? null,
    warnings: draft.warnings ?? [],
  }
}

export async function loadConversation(id: string): Promise<Conversation> {
  const dto = await unwrap(api.GET('/api/assistant/conversations/{id}', { params: { path: { id } } }))
  return {
    id: dto.id,
    title: dto.title,
    notice: dto.notice ?? null,
    costInr: dto.costInr,
    items: dto.items.map((item) => ({
      kind: item.kind,
      text: item.text ?? null,
      tool: item.tool ? toTool(item.tool) : null,
      draft: item.draft ? toDraft(item.draft) : null,
      interrupted: item.interrupted,
    })),
  }
}

export async function deleteConversation(id: string): Promise<void> {
  await unwrap(api.DELETE('/api/assistant/conversations/{id}', { params: { path: { id } } }))
}

export interface ReplyHandlers {
  onStarted: (id: string, title: string) => void
  onText: (delta: string) => void
  onTool: (tool: AssistantTool) => void
  onDraft: (draft: AssistantDraft) => void
  onDone: (usage: AiUsage, notice: string | null) => void
  onError: (message: string) => void
}

/** Sends a message and dispatches the streamed reply. Aborting the signal stops the reply on the server too. */
export function sendMessage(conversationId: string | null, message: string, handlers: ReplyHandlers, signal: AbortSignal) {
  return postEventStream(
    '/api/assistant/messages',
    { conversationId, message },
    (event) => {
      const data = JSON.parse(event.data) as Record<string, unknown>
      switch (event.event) {
        case 'conversation':
          handlers.onStarted(data.conversationId as string, data.title as string)
          break
        case 'text':
          handlers.onText(data.delta as string)
          break
        case 'tool':
          handlers.onTool(toTool(data as unknown as AssistantTool))
          break
        case 'draft':
          handlers.onDraft(toDraft(data as unknown as AssistantDraft))
          break
        case 'done':
          handlers.onDone(data.usage as AiUsage, (data.notice as string | undefined) ?? null)
          break
        case 'error':
          handlers.onError((data.message as string | undefined) ?? msg('Something went wrong. Please try again.'))
          break
      }
    },
    signal,
  )
}
