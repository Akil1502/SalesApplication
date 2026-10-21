import { useQueryClient } from '@tanstack/react-query'
import { CloudOff, RotateCcw, WifiOff } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { connection, useOnline, useServerUnreachable } from '@/lib/connection'
import { t } from '@/lib/i18n'

/**
 * A slim notice under the top bar while the browser is offline or the server can't be reached. While the server is
 * unreachable it checks every few seconds, and when it answers again the notice goes and the page's data reloads.
 */
export function ConnectionNotice() {
  const online = useOnline()
  const unreachable = useServerUnreachable()
  const queryClient = useQueryClient()
  const wasDown = useRef(false)

  // Check the server's health until it answers.
  useEffect(() => {
    if (!unreachable || !online) return
    const timer = setInterval(() => {
      fetch('/health', { cache: 'no-store' })
        .then((response) => {
          if (response.ok) connection.reportSuccess()
        })
        .catch(() => {})
    }, 8000)
    return () => clearInterval(timer)
  }, [unreachable, online])

  // Back again: reload what's on screen and say so.
  const down = unreachable || !online
  useEffect(() => {
    if (down) {
      wasDown.current = true
      return
    }
    if (wasDown.current) {
      wasDown.current = false
      void queryClient.refetchQueries({ type: 'active' })
      toast.success(t('Back online'))
    }
  }, [down, queryClient])

  return (
    <AnimatePresence>
      {down && (
        <motion.div
          key={online ? 'unreachable' : 'offline'}
          role="status"
          className="sticky top-14 z-20 overflow-hidden border-b border-amber-500/30 bg-amber-50/90 text-amber-950 backdrop-blur dark:bg-amber-950/80 dark:text-amber-100"
          initial={{ height: 0, opacity: 0 }}
          animate={{ height: 'auto', opacity: 1 }}
          exit={{ height: 0, opacity: 0 }}
          transition={{ duration: 0.3, ease: [0.16, 1, 0.3, 1] }}
        >
          <div className="flex flex-wrap items-center gap-2 px-4 py-2 text-sm md:px-6">
            {online ? <CloudOff className="size-4 shrink-0" /> : <WifiOff className="size-4 shrink-0" />}
            <span className="flex-1">
              {online
                ? t("Can't reach the SalesApp server. Checking again every few seconds; nothing you've saved is lost.")
                : t("You're offline. Changes can't be saved until the connection is back.")}
            </span>
            {online && (
              <span className="flex items-center gap-2">
                <Spinner className="size-3.5" />
                <Button size="xs" variant="outline" className="bg-transparent" onClick={() => void queryClient.refetchQueries({ type: 'active' })}>
                  <RotateCcw /> {t('Try now')}
                </Button>
              </span>
            )}
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}
