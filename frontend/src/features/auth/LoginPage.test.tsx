import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { AuthProvider } from './AuthProvider'
import { authStore } from './auth-store'
import { LoginPage } from './LoginPage'

function renderLogin() {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <LoginPage /> },
      { path: '/', element: <p>Dashboard home</p> },
    ],
    { initialEntries: ['/login'] },
  )
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('LoginPage', () => {
  it('validates the form before calling the API', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    renderLogin()

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('shows the server message when credentials are rejected', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      json(401, { message: 'Invalid email or password.', traceId: 't-1' }),
    )
    renderLogin()

    await userEvent.type(screen.getByLabelText('Email'), 'admin@salesapp.local')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
    expect(authStore.getSession()).toBeNull()
  })

  it('stores the session and navigates home on success', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      json(200, {
        accessToken: 'token-123',
        expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
        user: { id: '1', email: 'admin@salesapp.local', fullName: 'System Administrator', roles: ['Admin'] },
      }),
    )
    renderLogin()

    await userEvent.type(screen.getByLabelText('Email'), 'admin@salesapp.local')
    await userEvent.type(screen.getByLabelText('Password'), 'correct-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Dashboard home')).toBeInTheDocument()
    expect(authStore.getToken()).toBe('token-123')
    authStore.clear()
  })
})
