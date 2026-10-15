import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ReasonDialog } from './ReasonDialog'

describe('ReasonDialog', () => {
  it("doesn't submit a surrounding form (React bubbles portal events up the component tree)", async () => {
    // Regression: cancelling an order also re-saved it, because the dialog's submit reached the order form.
    const outerSubmit = vi.fn((e: React.FormEvent) => e.preventDefault())
    const onConfirm = vi.fn(async () => {})

    render(
      <form onSubmit={outerSubmit}>
        <ReasonDialog open onOpenChange={() => {}} title="Cancel order?" description="Test" confirmLabel="Cancel order" onConfirm={onConfirm} />
      </form>,
    )

    await userEvent.type(screen.getByLabelText('Reason'), 'Customer changed their mind')
    await userEvent.click(screen.getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).toHaveBeenCalledWith('Customer changed their mind')
    expect(outerSubmit).not.toHaveBeenCalled()
  })

  it('requires a reason', async () => {
    const onConfirm = vi.fn(async () => {})
    render(<ReasonDialog open onOpenChange={() => {}} title="Cancel?" description="Test" confirmLabel="Cancel it" onConfirm={onConfirm} />)

    await userEvent.click(screen.getByRole('button', { name: 'Cancel it' }))

    expect(await screen.findByText('Give a reason.')).toBeInTheDocument()
    expect(onConfirm).not.toHaveBeenCalled()
  })
})
