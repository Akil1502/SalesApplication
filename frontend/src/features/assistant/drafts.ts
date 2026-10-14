/**
 * Links that open a drafted message in the user's own email or WhatsApp, ready to review and send. The app never
 * sends anything itself. Recipients come from the customer record (the server fills them in), never from Claude.
 */

/** mailto: with subject and body; line breaks become CRLF as mail clients expect. */
export function mailtoUrl(email: string, subject: string | null, body: string): string {
  const params = [subject ? `subject=${encodeURIComponent(subject)}` : null, `body=${encodeURIComponent(body.replace(/\r?\n/g, '\r\n'))}`]
  return `mailto:${encodeURIComponent(email).replace(/%40/g, '@')}?${params.filter(Boolean).join('&')}`
}

/**
 * The international number wa.me expects: digits only, with India's 91 added to a 10-digit mobile number.
 * Null when the number can't be a phone number.
 */
export function whatsappNumber(phone: string): string | null {
  let digits = phone.replace(/\D/g, '')
  if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1)
  if (digits.length === 10) digits = `91${digits}`
  return digits.length >= 11 && digits.length <= 15 ? digits : null
}

export function whatsappUrl(phone: string, text: string): string | null {
  const number = whatsappNumber(phone)
  return number ? `https://wa.me/${number}?text=${encodeURIComponent(text)}` : null
}

/** What Copy puts on the clipboard: the subject line (for email) and the body. */
export function draftText(subject: string | null, body: string): string {
  return subject ? `Subject: ${subject}\n\n${body}` : body
}
