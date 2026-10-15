import { Sparkles } from 'lucide-react'
import { lazy, Suspense, useState } from 'react'
import { useLocation } from 'react-router'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetTitle } from '@/components/ui/sheet'
import { Spinner } from '@/components/ui/spinner'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { useAssistant } from './assistant-store'

const AssistantChat = lazy(() => import('./AssistantPanel'))

/**
 * The top-bar button and the slide-over chat panel, available on every screen. Hidden while AI is switched off.
 * A reply keeps going if the panel is closed; a dot on the button shows it's still writing. Following a link from
 * the panel (to a page, or to Ask your data) closes it, so the page isn't left hidden behind it.
 */
export function AssistantLauncher() {
  const status = useAiStatus()
  const location = useLocation()
  // Open only on the navigation it was opened at: any navigation closes the panel.
  const [openAt, setOpenAt] = useState<string | null>(null)
  const open = openAt === location.key
  const { status: chat } = useAssistant()
  const { can } = useAuth()
  if (!status.data?.assistant || !can(Permissions.AssistantUse)) return null

  return (
    <Sheet open={open} onOpenChange={(next) => setOpenAt(next ? location.key : null)}>
      <Button variant="outline" size="sm" onClick={() => setOpenAt(location.key)} className="relative">
        <Sparkles /> Assistant
        {chat === 'replying' && !open && (
          <span className="absolute -top-1 -right-1 size-2.5 animate-pulse rounded-full bg-primary" role="status" aria-label="The assistant is replying" />
        )}
      </Button>
      <SheetContent className="gap-0 p-0 data-[side=right]:w-full data-[side=right]:sm:max-w-xl">
        <Suspense
          fallback={
            <div className="flex flex-1 items-center justify-center gap-2 text-sm text-muted-foreground">
              <SheetTitle className="sr-only">Assistant</SheetTitle>
              <Spinner className="size-4" /> Loading…
            </div>
          }
        >
          <AssistantChat />
        </Suspense>
      </SheetContent>
    </Sheet>
  )
}
