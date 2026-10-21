/// <reference types="node" />
import { join } from 'node:path'
import tamil from '@/locales/ta.json'
import { placeholders, scan } from './i18n-scan'

type Entry = string | { one: string; other: string }

const catalog = tamil as Record<string, Entry>
// Tests run from the frontend folder (import.meta.url isn't a file: URL under jsdom).
const result = scan(join(process.cwd(), 'src'))
const where = (m: { file: string; line: number }) => `${m.file}:${m.line}`

describe('Tamil translation', () => {
  it('marks every text people see for translation', () => {
    expect(result.unmarked.map((u) => `${where(u)}  ${u.text}`)).toEqual([])
  })

  it('has Tamil for every marked text', () => {
    const missing = [...new Map(result.marked.filter((m) => !catalog[m.key]).map((m) => [m.key, `${m.key}  (${where(m)})`])).values()]
    expect(missing).toEqual([])
  })

  it('keeps every {placeholder} in the Tamil', () => {
    const wrong = result.marked.flatMap((m) => {
      const entry = catalog[m.key]
      if (!entry) return []
      const expected = placeholders(m.english)
      const forms = typeof entry === 'string' ? [entry] : [entry.other]
      // A singular may say "one" in words instead of {count}.
      const singular = typeof entry === 'string' ? [] : [entry.one]
      const bad = forms.some((f) => placeholders(f).join() !== expected.join()) || singular.some((f) => placeholders(f).some((p) => !expected.includes(p!)))
      return bad ? [`${m.key}  →  ${JSON.stringify(entry)}`] : []
    })
    expect([...new Set(wrong)]).toEqual([])
  })

  it('has no Tamil left over for text the app no longer shows', () => {
    const used = new Set(result.marked.map((m) => m.key))
    expect(Object.keys(catalog).filter((key) => !used.has(key))).toEqual([])
  })

  it('translates while rendering, never when a file loads', () => {
    expect(result.moduleLevel.map((m) => `${where(m)}  ${m.key}`)).toEqual([])
  })
})
