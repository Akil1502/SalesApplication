import { describe, expect, it } from 'vitest'
import { draftText, mailtoUrl, whatsappNumber, whatsappUrl } from './drafts'

describe('draft links', () => {
  it('builds a mailto link with the subject and a CRLF body', () => {
    expect(mailtoUrl('accounts@kavya.example', 'Payment reminder: INV/26-27/00042', 'Dear Kavya,\nPlease pay ₹2,360.00.')).toBe(
      'mailto:accounts@kavya.example?subject=Payment%20reminder%3A%20INV%2F26-27%2F00042&body=Dear%20Kavya%2C%0D%0APlease%20pay%20%E2%82%B92%2C360.00.',
    )
  })

  it('leaves out an empty subject', () => {
    expect(mailtoUrl('a@b.example', null, 'Hi')).toBe('mailto:a@b.example?body=Hi')
  })

  it('turns Indian phone numbers into WhatsApp numbers', () => {
    expect(whatsappNumber('+91 98765 43210')).toBe('919876543210')
    expect(whatsappNumber('9876543210')).toBe('919876543210')
    expect(whatsappNumber('09876543210')).toBe('919876543210')
    expect(whatsappNumber('+44 20 7946 0958')).toBe('442079460958')
    expect(whatsappNumber('12345')).toBeNull()
  })

  it('builds a wa.me link with the message', () => {
    expect(whatsappUrl('+919876543210', 'Hello & thanks')).toBe('https://wa.me/919876543210?text=Hello%20%26%20thanks')
    expect(whatsappUrl('n/a', 'Hello')).toBeNull()
  })

  it('copies the subject with an email', () => {
    expect(draftText('Payment reminder', 'Dear Kavya')).toBe('Subject: Payment reminder\n\nDear Kavya')
    expect(draftText(null, 'Hi')).toBe('Hi')
  })
})
