// Translation report: `node scripts/i18n.ts [folder] [--missing]` lists text not yet marked for translation, for the
// whole app or one folder under src (e.g. features/sales); --missing also lists marked text with no Tamil yet.
// `--where` prints every marked text as JSON instead ({ key: { english, one?, where: [file:line] } }), for the Excel
// review sheet (scripts/translations.cs at the repo root).
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { scan } from '../src/test/i18n-scan.ts'

const root = fileURLToPath(new URL('../src', import.meta.url))
const args = process.argv.slice(2)
const folder = args.find((a) => !a.startsWith('--'))?.replace(/\\/g, '/').replace(/^src\//, '')
const read = (url: URL) => JSON.parse(readFileSync(url, 'utf8')) as Record<string, unknown>

const catalog = read(new URL('../src/locales/ta.json', import.meta.url))

const result = scan(root)

if (args.includes('--where')) {
  const where: Record<string, { english: string; one?: string; where: string[] }> = {}
  for (const m of result.marked) {
    const entry = (where[m.key] ??= { english: m.english, ...(m.one && { one: m.one }), where: [] })
    entry.where.push(`${m.file}:${m.line}`)
  }
  process.stdout.write(JSON.stringify(where))
  process.exit(0)
}
const inFolder = (m: { file: string }) => !folder || m.file.startsWith(folder)
const unmarked = result.unmarked.filter(inFolder)
const missing = [...new Map(result.marked.filter(inFolder).filter((m) => !catalog[m.key]).map((m) => [m.key, m])).values()]
const moduleLevel = result.moduleLevel.filter(inFolder)

let lastFile = ''
for (const u of unmarked) {
  if (u.file !== lastFile) console.log(`\n${u.file}`)
  lastFile = u.file
  console.log(`  ${u.line}: ${u.text}`)
}
if (args.includes('--missing')) {
  if (missing.length) console.log('\nNo Tamil yet:')
  for (const m of missing) console.log(`  ${JSON.stringify(m.key)}${m.one ? ` (singular: ${JSON.stringify(m.one)})` : ''}  ${m.file}:${m.line}`)
}
for (const m of moduleLevel) console.log(`t() at module level: ${m.file}:${m.line} ${m.key}`)
console.log(`\n${unmarked.length} unmarked text(s); ${missing.length} marked text(s) with no Tamil yet; ${moduleLevel.length} t() call(s) at module level.`)
