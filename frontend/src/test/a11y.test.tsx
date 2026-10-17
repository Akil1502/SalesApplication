import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import axe from 'axe-core'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { DataTable } from '@/components/data-table/DataTable'
import { RouteError } from '@/components/errors/RouteError'
import { ShortcutsDialog } from '@/components/layout/ShortcutsDialog'
import { TooltipProvider } from '@/components/ui/tooltip'
import { AuthProvider } from '@/features/auth/AuthProvider'
import { LoginPage } from '@/features/auth/LoginPage'

/**
 * Automated accessibility checks (axe-core) on the shared building blocks. Colour contrast can't be measured in jsdom
 * (no rendering), so it's checked in a real browser instead; everything structural is checked here.
 */
async function expectAccessible(container: Element) {
  const results = await axe.run(container, { rules: { 'color-contrast': { enabled: false }, region: { enabled: false } } })
  const problems = results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`)
  expect(problems).toEqual([])
}

function withProviders(ui: React.ReactNode, path = '/') {
  const router = createMemoryRouter([{ path: '*', element: ui }], { initialEntries: [path] })
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthProvider>
        <TooltipProvider>
          <RouterProvider router={router} />
        </TooltipProvider>
      </AuthProvider>
    </QueryClientProvider>,
  )
}

describe('accessibility', () => {
  it('the sign-in page', async () => {
    const { container } = withProviders(<LoginPage />, '/login')
    await screen.findByRole('button', { name: 'Sign in' })
    await expectAccessible(container)
  })

  it('a list with clickable rows', async () => {
    const rows = [
      { id: 1, name: 'Pandian Knits', city: 'Tiruppur' },
      { id: 2, name: 'Kongu Distributors', city: 'Erode' },
    ]
    const { container } = withProviders(
      <DataTable
        columns={[
          { key: 'name', header: 'Customer', cell: (r: (typeof rows)[number]) => r.name, sortKey: 'name' },
          { key: 'city', header: 'City', cell: (r: (typeof rows)[number]) => r.city },
        ]}
        rows={rows}
        getRowKey={(r) => r.id}
        isLoading={false}
        onRowClick={vi.fn()}
        sort="name"
        onSortChange={vi.fn()}
      />,
    )
    await screen.findByText('Pandian Knits')
    await expectAccessible(container)
  })

  it('the keyboard shortcuts sheet', async () => {
    withProviders(<ShortcutsDialog open onOpenChange={vi.fn()} />)
    const dialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })
    await expectAccessible(dialog)
  })

  it('the error screen', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    function Broken(): React.ReactNode {
      throw new Error('Test failure')
    }
    const router = createMemoryRouter([{ path: '/', element: <Broken />, errorElement: <RouteError /> }])
    const { container } = render(<RouterProvider router={router} />)
    await screen.findByText('Something went wrong on this page')
    await expectAccessible(container)
  })
})
