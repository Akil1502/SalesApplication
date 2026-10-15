import { describe, expect, it } from 'vitest'
import { createSseParser, type SseEvent } from './sse'

function parseAll(chunks: string[]): SseEvent[] {
  const events: SseEvent[] = []
  const parse = createSseParser((e) => events.push(e))
  chunks.forEach(parse)
  return events
}

describe('createSseParser', () => {
  it('reads named events with JSON data', () => {
    expect(parseAll(['event: step\ndata: {"kind":"Query"}\n\nevent: answer\ndata: {"summary":"ok"}\n\n'])).toEqual([
      { event: 'step', data: '{"kind":"Query"}' },
      { event: 'answer', data: '{"summary":"ok"}' },
    ])
  })

  it('copes with chunks split anywhere, including mid-line and between the two newlines', () => {
    const text = 'event: step\ndata: {"message":"தமிழ் 1"}\n\nevent: answer\ndata: {"x":2}\n\n'
    const chunks = text.split('').reduce<string[]>((acc, ch, i) => (i % 3 === 0 ? [...acc, ch] : [...acc.slice(0, -1), acc[acc.length - 1] + ch]), [])
    expect(parseAll(chunks)).toEqual([
      { event: 'step', data: '{"message":"தமிழ் 1"}' },
      { event: 'answer', data: '{"x":2}' },
    ])
  })

  it('joins multi-line data, defaults the event name, and ignores comments and CRLF', () => {
    expect(parseAll([': keep-alive\r\n\r\ndata: line one\r\ndata: line two\r\n\r\n'])).toEqual([{ event: 'message', data: 'line one\nline two' }])
  })

  it('waits for the blank line before emitting', () => {
    const events: SseEvent[] = []
    const parse = createSseParser((e) => events.push(e))
    parse('event: answer\ndata: {}\n')
    expect(events).toHaveLength(0)
    parse('\n')
    expect(events).toHaveLength(1)
  })
})
