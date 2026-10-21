import { ChevronDown, Copy, House, RefreshCw, RotateCcw } from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import { Link, useLocation, useNavigate, useRouteError } from 'react-router'
import { Button } from '@/components/ui/button'
import { msg, t } from '@/lib/i18n'
import { cn } from '@/lib/utils'
import { ErrorArt } from './ErrorArt'
import { errorDetails, errorKind, type ErrorKind } from './error-kind'

const copy: Record<ErrorKind, { title: string; body: string }> = {
  'new-version': {
    title: msg('SalesApp has been updated'),
    body: msg('A newer version was installed while this page was open. Reload to carry on with it; nothing you saved is lost.'),
  },
  unreachable: {
    title: msg("Can't reach the SalesApp server"),
    body: msg('The server may be restarting or the network is down. Try again in a moment.'),
  },
  'not-found': {
    title: msg("This page or record doesn't exist"),
    body: msg('It may have been removed, or the link is mistyped.'),
  },
  forbidden: {
    title: msg("You don't have access to this"),
    body: msg('Ask an administrator if you need it.'),
  },
  broken: {
    title: msg('Something went wrong on this page'),
    body: msg('Try again. If it keeps happening, copy the details below and send them to whoever looks after SalesApp.'),
  },
}

/**
 * Shown in place of a page (or the record in the side panel, or the whole app) when it fails to load or crashes,
 * instead of React Router's developer screen. Offers what helps: trying again, reloading for a new version, or
 * going home, with the technical details tucked away for support.
 */
export function RouteError({ variant = 'page' }: { variant?: 'page' | 'panel' | 'full' }) {
  const error = useRouteError()
  const navigate = useNavigate()
  const location = useLocation()
  const [copied, setCopied] = useState(false)
  const kind = errorKind(error)
  const details = errorDetails(error)
  const text = copy[kind]

  // Navigating to the same address clears the error and renders the page afresh.
  const retry = () => navigate({ pathname: location.pathname, search: location.search }, { replace: true })

  const content = (
    <motion.div
      role="alert"
      className="flex max-w-md flex-col items-center gap-4 text-center"
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: [0.16, 1, 0.3, 1] }}
    >
      <ErrorArt />
      <div className="space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">{t(text.title)}</h2>
        <p className="text-sm text-muted-foreground">{t(text.body)}</p>
      </div>
      <div className="flex flex-wrap justify-center gap-2">
        {kind === 'new-version' ? (
          <Button onClick={() => window.location.reload()}>
            <RefreshCw /> {t('Reload')}
          </Button>
        ) : (
          kind !== 'not-found' &&
          kind !== 'forbidden' && (
            <Button onClick={retry}>
              <RotateCcw /> {t('Try again')}
            </Button>
          )
        )}
        <Button asChild variant="outline">
          <Link to="/">
            <House /> {t('Dashboard')}
          </Link>
        </Button>
      </div>
      {kind === 'broken' && (
        <details className="group w-full rounded-lg border bg-muted/40 text-left text-xs">
          <summary className="flex cursor-pointer list-none items-center gap-1 px-3 py-2 font-medium text-muted-foreground">
            <ChevronDown className="size-3.5 transition-transform group-open:rotate-180" /> {t('Technical details')}
          </summary>
          <div className="space-y-2 border-t px-3 py-2">
            <pre className="font-mono break-words whitespace-pre-wrap">{details}</pre>
            <Button
              size="xs"
              variant="outline"
              onClick={() =>
                void navigator.clipboard?.writeText(`${details}\nPage: ${location.pathname}${location.search}\nTime: ${new Date().toISOString()}`).then(() => setCopied(true))
              }
            >
              <Copy /> {copied ? t('Copied') : t('Copy details')}
            </Button>
          </div>
        </details>
      )}
    </motion.div>
  )

  return (
    <div className={cn('flex items-center justify-center p-6', variant === 'full' ? 'min-h-svh bg-background' : variant === 'panel' ? 'min-h-[60vh]' : 'min-h-[65vh]')}>
      {content}
    </div>
  )
}
