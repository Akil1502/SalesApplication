import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuth } from './auth-context'
import { AuthProvider } from './AuthProvider'
import { authStore } from './auth-store'
import { LoginPage } from './LoginPage'
import { RequireAuth } from './RequireAuth'
import { landingPage } from './sign-in-state'

const users: Record<string, { id: string; fullName: string }> = {
  'admin@salesapp.local': { id: 'u-admin', fullName: 'System Administrator' },
  'priya@salesapp.local': { id: 'u-priya', fullName: 'Priya Raman' },
}

function AuditPage() {
  const { logout } = useAuth()
  return (
    <>
      <p>Audit log page</p>
      <button type="button" onClick={logout}>
        Sign out
      </button>
    </>
  )
}

function renderApp(start: string) {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <LoginPage /> },
      {
        element: <RequireAuth />,
        children: [
          { path: '/', element: <p>Dashboard home</p> },
          { path: '/admin/audit', element: <AuditPage /> },
        ],
      },
    ],
    { initialEntries: [start] },
  )
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
}

async function signIn(email: string) {
  await userEvent.type(await screen.findByLabelText('Email'), email)
  await userEvent.type(screen.getByLabelText('Password'), 'any-password')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
}

describe('the page after signing in', () => {
  beforeEach(() => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const { email } = (await (input as Request).json()) as { email: string }
      const user = users[email]!
      const body = {
        accessToken: `token-${user.id}`,
        expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
        user: { ...user, email, role: 'Administrator', permissions: [] },
      }
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
  })

  afterEach(() => {
    authStore.clear()
    authStore.takeSignOutReason()
  })

  it('opens the page a link pointed at, for whoever signs in', async () => {
    renderApp('/admin/audit')
    await signIn('priya@salesapp.local')
    expect(await screen.findByText('Audit log page')).toBeInTheDocument()
  })

  it("starts the next person at home after a sign-out, not on the last person's page", async () => {
    renderApp('/admin/audit')
    await signIn('admin@salesapp.local')
    await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    await signIn('priya@salesapp.local')
    expect(await screen.findByText('Dashboard home')).toBeInTheDocument()
  })

  it('takes the same person back to their page after their session expired, but not someone else', async () => {
    renderApp('/admin/audit')
    await signIn('admin@salesapp.local')
    await screen.findByText('Audit log page')

    act(() => authStore.clear('expired'))
    expect(await screen.findByText('Your session expired. Please sign in again.')).toBeInTheDocument()
    await signIn('admin@salesapp.local')
    expect(await screen.findByText('Audit log page')).toBeInTheDocument()

    act(() => authStore.clear('expired'))
    await signIn('priya@salesapp.local')
    expect(await screen.findByText('Dashboard home')).toBeInTheDocument()
  })
})

describe('landingPage', () => {
  it('goes home when nothing asked for sign-in', () => {
    expect(landingPage(null, 'u-1')).toBe('/')
    expect(landingPage({ userId: 'u-1' }, 'u-1')).toBe('/')
  })

  it("goes back only to a page that wasn't someone else's", () => {
    expect(landingPage({ from: '/invoices/5/print' }, 'u-1')).toBe('/invoices/5/print')
    expect(landingPage({ from: '/admin/audit', userId: 'u-1' }, 'u-1')).toBe('/admin/audit')
    expect(landingPage({ from: '/admin/audit', userId: 'u-1' }, 'u-2')).toBe('/')
  })
})
