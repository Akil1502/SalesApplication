import { describe, expect, it, vi } from 'vitest'
import { ApiRequestError, applyFieldErrors, errorMessage, unwrap } from './errors'

const response = (status: number) => new Response(null, { status, statusText: status === 500 ? 'Server Error' : '' })

describe('unwrap', () => {
  it('returns data for a successful response', async () => {
    await expect(unwrap(Promise.resolve({ data: { id: 1 }, response: response(200) }))).resolves.toEqual({ id: 1 })
  })

  it('throws ApiRequestError carrying the server ApiError body', async () => {
    const error = { message: 'Invalid email or password.', traceId: 't-1' }
    const failure = unwrap(Promise.resolve({ error, response: response(401) }))

    await expect(failure).rejects.toBeInstanceOf(ApiRequestError)
    await expect(failure).rejects.toMatchObject({ status: 401, body: error })
  })

  it('falls back to a generic message when the body is not an ApiError', async () => {
    await expect(unwrap(Promise.resolve({ error: 'boom', response: response(500) }))).rejects.toMatchObject({
      status: 500,
      message: 'The server returned 500 Server Error.',
    })
  })

  it('reports network failures as a friendly status-0 error', async () => {
    await expect(unwrap(Promise.reject(new TypeError('Failed to fetch')))).rejects.toMatchObject({ status: 0 })
  })
})

describe('applyFieldErrors', () => {
  it('maps server field errors onto known form fields only', () => {
    const setError = vi.fn()
    const error = new ApiRequestError(400, {
      message: 'Some fields need attention.',
      traceId: 't',
      errors: { gstin: ['Invalid GSTIN checksum.'], unknownField: ['ignored'] },
    })

    expect(applyFieldErrors(error, setError, ['gstin', 'name'])).toBe(true)
    expect(setError).toHaveBeenCalledExactlyOnceWith('gstin', { type: 'server', message: 'Invalid GSTIN checksum.' })
  })

  it('returns false when there are no field errors to show', () => {
    expect(applyFieldErrors(new Error('x'), vi.fn(), ['name'])).toBe(false)
  })
})

describe('errorMessage', () => {
  it('prefers the server message', () => {
    expect(errorMessage(new ApiRequestError(409, { message: 'Changed by someone else.', traceId: '' }))).toBe(
      'Changed by someone else.',
    )
  })

  it('has a fallback for unknown values', () => {
    expect(errorMessage(undefined)).toBe('Something went wrong. Please try again.')
  })
})
