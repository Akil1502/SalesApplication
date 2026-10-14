import { authStore } from '@/features/auth/auth-store'
import { ApiRequestError, isApiError } from '@/lib/api/errors'

export interface SseEvent {
  event: string
  data: string
}

/**
 * An incremental parser for a text/event-stream body. Feed it chunks as they arrive (they can split anywhere,
 * even mid-line); it calls `onEvent` once per complete event. Comments and fields other than event/data are ignored.
 */
export function createSseParser(onEvent: (event: SseEvent) => void): (chunk: string) => void {
  let buffer = ''
  return (chunk) => {
    buffer += chunk.replace(/\r\n?/g, '\n')
    let end: number
    while ((end = buffer.indexOf('\n\n')) >= 0) {
      const frame = buffer.slice(0, end)
      buffer = buffer.slice(end + 2)
      let event = 'message'
      const data: string[] = []
      for (const line of frame.split('\n')) {
        if (line === '' || line.startsWith(':')) continue
        const colon = line.indexOf(':')
        const field = colon < 0 ? line : line.slice(0, colon)
        let value = colon < 0 ? '' : line.slice(colon + 1)
        if (value.startsWith(' ')) value = value.slice(1)
        if (field === 'event') event = value
        else if (field === 'data') data.push(value)
      }
      if (data.length > 0) onEvent({ event, data: data.join('\n') })
    }
  }
}

/**
 * POSTs JSON and reads a server-sent event stream. EventSource can't send a body or an Authorization header, so
 * this uses fetch and keeps the JWT in the header (never in the URL). Aborting the signal stops the request, which
 * the server sees as a cancelled request and stops work (and AI spend).
 */
export async function postEventStream(path: string, body: unknown, onEvent: (event: SseEvent) => void, signal?: AbortSignal): Promise<void> {
  const token = authStore.getToken()
  let response: Response
  try {
    response = await globalThis.fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: JSON.stringify(body),
      signal,
    })
  } catch (error) {
    if (signal?.aborted) throw error
    throw new ApiRequestError(0, { message: 'Could not reach the server. Check that the API is running and try again.', traceId: '' })
  }

  if (response.status === 401) authStore.clear('expired')
  if (!response.ok || !response.body) {
    const error: unknown = await response.json().catch(() => null)
    throw new ApiRequestError(
      response.status,
      isApiError(error) ? error : { message: `The server returned ${response.status} ${response.statusText}.`, traceId: '' },
    )
  }

  const parse = createSseParser(onEvent)
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader()
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    parse(value)
  }
}
