import { ChartColumn, CheckCircle2, Database, FileText, History, Package, Plus, Search, Send, Sparkles, Square, Trash2, TriangleAlert, Users } from 'lucide-react'
import { Fragment, useEffect, useRef, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import type { AskHandoff } from '@/features/ask/api'
import { errorMessage } from '@/lib/api/errors'
import { formatDate, formatINR } from '@/lib/format'
import { msg, t } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'
import { cn } from '@/lib/utils'
import { useConversations, type AssistantItem, type AssistantTool } from './api'
import { assistantStore, useAssistant } from './assistant-store'
import { dataQuestionTurns } from './data-questions'
import { DraftCard } from './DraftCard'
import { MessageMarkdown } from './MessageMarkdown'

// Tasks about a customer or a document; questions about figures are Ask your data's job.
const suggestions = [
  msg('Draft a payment reminder for our most overdue customer'),
  msg('Write a follow-up for our oldest open quotation'),
  msg('How is Murugan Garments doing this year?'),
  msg('What did Thangam Silks order last time?'),
]

/** Opens Ask your data with the question filled in, for a table, chart and Excel export of the same figures. */
function AskDataLink({ question }: { question: string }) {
  const navigate = useNavigate()
  return (
    <Button
      variant="outline"
      size="sm"
      className="h-auto self-start py-1.5 text-left whitespace-normal"
      onClick={() => void navigate('/ask', { state: { question } satisfies AskHandoff })}
    >
      <ChartColumn />
      <span>
        <T text="Open in Ask your data {details}" values={{ details: <span className="text-muted-foreground">{t('for a table, chart and Excel export')}</span> }} />
      </span>
    </Button>
  )
}

const toolIcons: Record<string, ReactNode> = {
  search_customers: <Users className="size-3.5" />,
  get_customer: <Users className="size-3.5" />,
  list_invoices: <FileText className="size-3.5" />,
  get_document: <FileText className="size-3.5" />,
  search_products: <Package className="size-3.5" />,
  query_sales_data: <Database className="size-3.5" />,
}

function ToolLine({ tool, replying }: { tool: AssistantTool; replying: boolean }) {
  const running = tool.succeeded === null && replying
  const icon = running ? (
    <Spinner className="size-3.5" />
  ) : tool.succeeded === false ? (
    <TriangleAlert className="size-3.5 text-amber-600" />
  ) : (
    (toolIcons[tool.name] ?? <Search className="size-3.5" />)
  )
  return (
    <div className="flex flex-col gap-1 text-xs text-muted-foreground">
      <span className="flex items-center gap-1.5">
        {icon}
        <span className={cn(tool.succeeded === false && 'text-amber-800 dark:text-amber-300')}>{tool.label}</span>
        {tool.succeeded && <CheckCircle2 className="size-3 text-viz-up" />}
      </span>
      {tool.detail && (
        <details className="ml-5">
          <summary className="cursor-pointer">SQL</summary>
          <pre className="mt-1 overflow-x-auto rounded-md bg-muted p-2 font-mono text-[11px] whitespace-pre-wrap">{tool.detail}</pre>
        </details>
      )}
    </div>
  )
}

function Item({ item, replying }: { item: AssistantItem; replying: boolean }) {
  switch (item.kind) {
    case 'User':
      return (
        <div className="ml-auto max-w-[85%] rounded-2xl rounded-br-sm bg-primary px-3 py-2 text-sm whitespace-pre-wrap text-primary-foreground">
          {item.text}
        </div>
      )
    case 'Assistant':
      return (
        <div className="flex flex-col gap-1">
          <MessageMarkdown text={item.text ?? ''} />
          {item.interrupted && <p className="text-xs text-muted-foreground">{t('Stopped before the reply was finished.')}</p>}
        </div>
      )
    case 'Tool':
      return item.tool ? <ToolLine tool={item.tool} replying={replying} /> : null
    case 'Draft':
      return item.draft ? <DraftCard draft={item.draft} /> : null
  }
}

function HistoryMenu() {
  const [open, setOpen] = useState(false)
  const conversations = useConversations(open)
  const { status, conversationId } = useAssistant()
  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <Tooltip>
        <TooltipTrigger asChild>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon-sm" aria-label={t('Recent conversations')} disabled={status === 'replying'}>
              <History />
            </Button>
          </DropdownMenuTrigger>
        </TooltipTrigger>
        <TooltipContent>{t('Recent conversations')}</TooltipContent>
      </Tooltip>
      <DropdownMenuContent align="end" className="w-80">
        <DropdownMenuLabel>{t('Recent conversations')}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {conversations.isPending && <p className="px-2 py-1.5 text-sm text-muted-foreground">{t('Loading…')}</p>}
        {conversations.isError && <p className="px-2 py-1.5 text-sm text-destructive">{errorMessage(conversations.error)}</p>}
        {conversations.data?.length === 0 && <p className="px-2 py-1.5 text-sm text-muted-foreground">{t('No conversations yet.')}</p>}
        {conversations.data?.map((c) => (
          <DropdownMenuItem key={c.id} onSelect={() => void assistantStore.open(c.id)} className={cn('flex flex-col items-start gap-0', c.id === conversationId && 'bg-accent')}>
            <span className="line-clamp-1 w-full">{c.title}</span>
            <span className="text-xs text-muted-foreground">{formatDate(c.updatedAtUtc)}</span>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/** The chat itself: conversation, lookups, drafts and the message box. Loaded when the panel first opens. */
export default function AssistantChat() {
  const { conversationId, title, items, status, error, notice, costInr, lastCostInr } = useAssistant()
  const [message, setMessage] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)
  const navigate = useNavigate()
  const scroller = useRef<HTMLDivElement>(null)
  const stickToBottom = useRef(true)
  const replying = status === 'replying'
  const last = items.at(-1)
  const thinking = replying && last?.kind !== 'Assistant'
  const dataTurns = dataQuestionTurns(items, replying)

  useEffect(() => {
    void assistantStore.resume()
  }, [])

  // Follow the reply as it streams, unless the user has scrolled up to read something.
  useEffect(() => {
    const element = scroller.current
    if (element && stickToBottom.current) element.scrollTop = element.scrollHeight
  }, [items, status])

  function send(text: string) {
    if (!text.trim() || status !== 'idle') return
    setMessage('')
    stickToBottom.current = true
    void assistantStore.send(text)
  }

  async function remove() {
    if (!conversationId) return
    try {
      await assistantStore.remove(conversationId)
      toast.success(t('Conversation deleted.'))
    } catch (e) {
      toast.error(errorMessage(e))
    }
  }

  return (
    <>
      <SheetHeader className="flex-row items-center gap-2 border-b py-3 pr-12 pl-4">
        <Sparkles className="size-4 shrink-0 text-muted-foreground" />
        <div className="min-w-0 flex-1">
          <SheetTitle className="truncate text-sm">{title ?? t('Assistant')}</SheetTitle>
          <SheetDescription className="sr-only">
            {t('Look up customers, orders, invoices and payments, or have a message drafted for a customer.')}
          </SheetDescription>
        </div>
        <HistoryMenu />
        {conversationId && (
          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="icon-sm" aria-label={t('Delete this conversation')} disabled={replying} onClick={() => setConfirmDelete(true)}>
                <Trash2 />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{t('Delete this conversation')}</TooltipContent>
          </Tooltip>
        )}
        <Tooltip>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="icon-sm" aria-label={t('New conversation')} disabled={replying || items.length === 0} onClick={() => assistantStore.newChat()}>
              <Plus />
            </Button>
          </TooltipTrigger>
          <TooltipContent>{t('New conversation')}</TooltipContent>
        </Tooltip>
      </SheetHeader>

      <div
        ref={scroller}
        onScroll={(e) => {
          const element = e.currentTarget
          stickToBottom.current = element.scrollHeight - element.scrollTop - element.clientHeight < 80
        }}
        className="flex flex-1 flex-col gap-4 overflow-y-auto px-4 py-4"
        aria-live="polite"
      >
        {status === 'loading' && (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <Spinner className="size-4" /> {t('Opening the conversation…')}
          </p>
        )}

        {items.length === 0 && status === 'idle' && (
          <div className="flex flex-col gap-3">
            <p className="text-sm text-muted-foreground">
              {t('Look up a customer, order, invoice or payment, in English or Tamil, and have payment reminders, follow-ups and quotation notes drafted; you review and send them yourself.')}
            </p>
            <p className="text-sm text-muted-foreground">
              <T
                text="For figures as a table or chart, such as monthly sales or top customers, use {link}."
                values={{
                  link: (
                    <Button variant="link" className="h-auto p-0 text-sm" onClick={() => void navigate('/ask')}>
                      {t('Ask your data')}
                    </Button>
                  ),
                }}
              />
            </p>
            <div className="flex flex-col items-start gap-2">
              {suggestions.map((suggestion) => (
                <Button key={suggestion} size="sm" variant="outline" className="h-auto py-1.5 text-left whitespace-normal" onClick={() => send(t(suggestion))}>
                  {t(suggestion)}
                </Button>
              ))}
            </div>
          </div>
        )}

        {items.map((item, i) => (
          <Fragment key={item.tool?.id ?? item.draft?.toolUseId ?? `${item.kind}-${i}`}>
            <Item item={item} replying={replying} />
            {dataTurns.has(i) && <AskDataLink question={dataTurns.get(i) ?? ''} />}
          </Fragment>
        ))}

        {thinking && (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <Spinner className="size-4" /> {t('Thinking…')}
          </p>
        )}

        {error && (
          <Alert variant="destructive">
            <AlertDescription>{t(error)}</AlertDescription>
          </Alert>
        )}
      </div>

      <div className="flex flex-col gap-2 border-t p-3">
        {notice && (
          <div className="flex items-center justify-between gap-2 rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
            <span>{notice}</span>
            <Button size="sm" variant="outline" className="h-7 shrink-0" disabled={replying} onClick={() => assistantStore.newChat()}>
              {t('New conversation')}
            </Button>
          </div>
        )}
        <form
          className="flex items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            send(message)
          }}
        >
          <Textarea
            aria-label={t('Message the assistant')}
            rows={2}
            maxLength={4000}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                send(message)
              }
            }}
            placeholder={t('Ask a question or ask for a draft…')}
            disabled={status === 'loading'}
            className="max-h-40 min-h-12 flex-1 resize-none"
          />
          {replying ? (
            <Button type="button" variant="outline" size="icon" aria-label={t('Stop')} onClick={() => assistantStore.stop()}>
              <Square />
            </Button>
          ) : (
            <Button type="submit" size="icon" aria-label={t('Send')} disabled={!message.trim() || status !== 'idle'}>
              <Send />
            </Button>
          )}
        </form>
        <p className="text-[11px] text-muted-foreground">
          {t('Reads your data, never changes it. Check figures before you rely on them.')}
          {costInr > 0 &&
            ' ' +
              (lastCostInr !== null
                ? t('This conversation: about {cost} (last reply {last}).', { cost: formatINR(costInr), last: formatINR(lastCostInr) })
                : t('This conversation: about {cost}.', { cost: formatINR(costInr) }))}
        </p>
      </div>

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title={t('Delete this conversation?')}
        description={t('Its messages and drafts are removed for good.')}
        confirmLabel={t('Delete')}
        destructive
        onConfirm={() => void remove()}
      />
    </>
  )
}
