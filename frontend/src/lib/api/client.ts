import createClient, { type Middleware } from 'openapi-fetch'
import { authStore } from '@/features/auth/auth-store'
import { acceptLanguage } from '@/lib/i18n'
import type { components, paths } from './schema'

/** Request/response shapes generated from the API's OpenAPI document (`npm run gen:api`). */
export type Schemas = components['schemas']

/**
 * Typed API client. Paths, bodies and responses are checked against the backend's OpenAPI
 * document at compile time. Same-origin in both dev (Vite proxy) and production (served by the API).
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? '',
  // Resolve fetch at call time rather than import time, so tests can stub it.
  fetch: (request) => globalThis.fetch(request),
})

const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = authStore.getToken()
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }
    // The server writes its messages (and Claude its answers) in the language on screen.
    request.headers.set('Accept-Language', acceptLanguage())
    return request
  },
  onResponse({ request, response }) {
    // A 401 on any call except login means the token expired or the account was disabled.
    if (response.status === 401 && !new URL(request.url).pathname.endsWith('/api/auth/login')) {
      authStore.clear('expired')
    }
    return response
  },
}

api.use(authMiddleware)
