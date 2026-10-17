import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { PreferencesProvider } from '@/components/fx/PreferencesProvider'
import { Toaster } from '@/components/ui/sonner'
import { TooltipProvider } from '@/components/ui/tooltip'
import { AuthProvider } from '@/features/auth/AuthProvider'
import { ApiRequestError } from '@/lib/api/errors'
import { connection } from '@/lib/connection'
import { router } from './routes'
import './index.css'

// Every request reports whether the server answered, for the "can't reach the server" notice.
const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError: connection.reportError, onSuccess: connection.reportSuccess }),
  mutationCache: new MutationCache({ onError: connection.reportError, onSuccess: connection.reportSuccess }),
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Retry network blips and 5xx once; never retry 4xx (validation, auth, not found).
      retry: (failureCount, error) =>
        failureCount < 1 && !(error instanceof ApiRequestError && error.status >= 400 && error.status < 500),
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <PreferencesProvider>
          <TooltipProvider>
            <RouterProvider router={router} />
            <Toaster richColors position="top-right" />
          </TooltipProvider>
        </PreferencesProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
