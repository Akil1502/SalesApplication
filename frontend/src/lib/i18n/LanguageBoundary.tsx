import { useQueryClient } from '@tanstack/react-query'
import { Fragment, useEffect, useRef, type ReactNode } from 'react'
import { useLanguage } from './index'

/**
 * Re-renders everything inside when the language changes, so every t() picks up the new language, and fetches
 * again what the server wrote in words (messages, notifications, AI write-ups). Saved settings aren't fetched
 * again: the language change itself may still be on its way to the server.
 */
export function LanguageBoundary({ children }: { children: ReactNode }) {
  const language = useLanguage()
  const queryClient = useQueryClient()
  const shown = useRef(language)

  useEffect(() => {
    if (shown.current === language) return
    shown.current = language
    void queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== 'me' })
  }, [language, queryClient])

  return <Fragment key={language}>{children}</Fragment>
}
