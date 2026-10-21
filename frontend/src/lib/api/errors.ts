import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { t } from '@/lib/i18n'
import type { Schemas } from './client'

export type ApiError = Schemas['ApiError']

/** Thrown by {@link unwrap} for any non-2xx response, carrying the server's ApiError body. */
export class ApiRequestError extends Error {
  readonly status: number
  readonly body: ApiError

  constructor(status: number, body: ApiError) {
    super(body.message)
    this.name = 'ApiRequestError'
    this.status = status
    this.body = body
  }
}

export function isApiError(value: unknown): value is ApiError {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as ApiError).message === 'string' &&
    typeof (value as ApiError).traceId === 'string'
  )
}

interface ClientResult<T> {
  data?: T
  error?: unknown
  response: Response
}

/**
 * Await an openapi-fetch call and return its data, or throw {@link ApiRequestError}.
 * Lets TanStack Query treat API failures as ordinary rejected promises.
 */
export async function unwrap<T>(call: Promise<ClientResult<T>>): Promise<T> {
  let result: ClientResult<T>
  try {
    result = await call
  } catch {
    throw new ApiRequestError(0, {
      message: t('Could not reach the server. Check that the API is running and try again.'),
      traceId: '',
    })
  }

  const { data, error, response } = result
  if (!response.ok || error !== undefined) {
    const body: ApiError = isApiError(error) ? error : { message: statusMessage(response), traceId: '' }
    throw new ApiRequestError(response.status, body)
  }
  return data as T
}

/**
 * A message for a failed response that carried no ApiError body. A gateway answering for a server that's down or
 * restarting (502, 503, 504) gets plain words rather than its status code.
 */
export function statusMessage(response: Pick<Response, 'status' | 'statusText'>): string {
  if (response.status >= 502 && response.status <= 504) return t("Can't reach the SalesApp server right now. It may be restarting; try again in a moment.")
  return t('The server returned {status} {text}.', { status: response.status, text: response.statusText })
}

/** A user-facing message for any thrown value. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiRequestError) return error.body.message
  if (error instanceof Error && error.message) return error.message
  return t('Something went wrong. Please try again.')
}

/**
 * Copy server-side field errors onto a react-hook-form form. Returns true if at least one error
 * matched a form field, so the caller knows whether to also show a general message.
 */
export function applyFieldErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): boolean {
  if (!(error instanceof ApiRequestError) || !error.body.errors) return false

  let matched = false
  for (const [field, messages] of Object.entries(error.body.errors)) {
    const formField = fields.find((f) => f === field)
    if (formField && messages.length > 0) {
      setError(formField, { type: 'server', message: messages[0] })
      matched = true
    }
  }
  return matched
}
