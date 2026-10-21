/// <reference types="node" />
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import ts from 'typescript'

/**
 * Reads the app's source to find (1) every text marked for translation, so src/locales/ta.json can be checked
 * against it, and (2) text shown to people that isn't marked yet. Used by src/test/i18n.test.ts and by
 * `node scripts/i18n.ts` (a report, and the Excel review sheet's list of where each text appears).
 *
 * Marked: t('…'), tc('context', '…'), tn(n, 'one', 'other'), msg('…'), x.t('…') from translator(),
 * docText(language, '…'), the text="…" of <T> and <DocText>, and the label="…" of <DocField>. Not marked yet: words
 * between JSX tags, text-like props (aria-label, title, placeholder, label, …Label, …Text, …), text-like object
 * fields (label, title, description, text, …), toast messages, sentences returned from functions, and the values of
 * objects named like labels. A line (or the line above it) with `i18n-ignore` is skipped.
 */

export interface Marked {
  key: string
  english: string
  file: string
  line: number
  context?: string
  one?: string
}

export interface Unmarked {
  file: string
  line: number
  text: string
}

export interface ScanResult {
  marked: Marked[]
  unmarked: Unmarked[]
  /** t() calls outside any function: they'd be fixed in the language on screen when the file loaded. */
  moduleLevel: Marked[]
}

const SKIP_DIRS = new Set(['test', 'locales'])
const SKIP_FILES = /\.test\.tsx?$|\.d\.ts$/

const TRANSLATE = new Set(['t', 'tc', 'tn', 'msg', 'docText'])
/** Components whose text prop is English to translate, and which prop that is. */
const TEXT_COMPONENTS: Record<string, string> = { T: 'text', DocText: 'text', DocField: 'label' }

const TEXT_NAME =
  /^(aria-label|aria-description|aria-valuetext|aria-roledescription|title|placeholder|alt|label|description|heading|hint|tooltip|message|caption|subtitle|summary|emptyText|header|text|body|detail|help|question|example|error)$|(Label|Text|Title|Description|Hint|Message|Placeholder|Heading|Caption)$/
const LABEL_OBJECT = /label|text|title|caption|message|word|name/i
const TOAST = /^toast(\.(success|error|info|warning|message|loading))?$/
const TEXT_FUNCTIONS = new Set(['useDocumentTitle', 'confirm', 'alert'])

/** Key names, acronyms and brand names that stay as they are in Tamil. */
const KEEP = /^(?:[A-Z0-9]{1,6}s?|Ctrl|Shift|Alt|Esc|Enter|Tab|Space|Excel|Claude|WhatsApp|Windows|SalesApp|Cmd|Del|Home|End|PgUp|PgDn|N\/A)$/

/** Whether a literal looks like words for people rather than a class name, key or code. */
function isWords(text: string): boolean {
  const words = text.replace(/\{\w+\}/g, ' ').trim()
  if (!/[A-Za-z]{2}/.test(words)) return false
  const tokens = words.split(/[\s,.:;!?()·•…/+—–-]+/).filter((w) => /[A-Za-z]/.test(w))
  return !tokens.every((w) => KEEP.test(w))
}

/** Stricter, for strings whose role is unknown (returned from a function, values in a label map). */
function isSentence(text: string): boolean {
  const s = text.trim()
  return isWords(s) && (/^[A-Z][a-z']*(\s|,|$)/.test(s) || /^[A-Z][a-z]+[A-Za-z]*$/.test(s) || (/\s/.test(s) && /[.!?…]$/.test(s)))
}

function files(dir: string): string[] {
  const out: string[] = []
  for (const name of readdirSync(dir)) {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) {
      if (!SKIP_DIRS.has(name)) out.push(...files(path))
    } else if (/\.tsx?$/.test(name) && !SKIP_FILES.test(name)) out.push(path)
  }
  return out
}

function literal(node: ts.Node | undefined): string | undefined {
  if (!node) return undefined
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return node.text
  if (ts.isJsxExpression(node)) return literal(node.expression)
  if (ts.isParenthesizedExpression(node)) return literal(node.expression)
  return undefined
}

function calleeName(expression: ts.Expression): string | undefined {
  if (ts.isIdentifier(expression)) return expression.text
  if (ts.isPropertyAccessExpression(expression)) {
    const target = calleeName(expression.expression)
    return target === undefined ? expression.name.text : `${target}.${expression.name.text}`
  }
  return undefined
}

function insideFunction(node: ts.Node): boolean {
  for (let n = node.parent; n; n = n.parent) {
    if (ts.isFunctionLike(n)) return true
  }
  return false
}

export function scanFile(path: string, root: string, source = readFileSync(path, 'utf8')): ScanResult {
  const file = relative(root, path).split(sep).join('/')
  const sf = ts.createSourceFile(path, source, ts.ScriptTarget.Latest, true, path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS)
  const lines = source.split('\n')
  const result: ScanResult = { marked: [], unmarked: [], moduleLevel: [] }
  const lineOf = (node: ts.Node) => sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1
  const ignored = (line: number) => /i18n-ignore/.test(lines[line - 1] ?? '') || /i18n-ignore/.test(lines[line - 2] ?? '')

  const mark = (node: ts.Node, english: string, extra: Partial<Marked> = {}) => {
    const entry: Marked = { key: extra.context ? `${extra.context}::${english}` : english, english, file, line: lineOf(node), ...extra }
    result.marked.push(entry)
    return entry
  }
  const flag = (node: ts.Node, text: string) => {
    const line = lineOf(node)
    if (!ignored(line)) result.unmarked.push({ file, line, text: text.trim().replace(/\s+/g, ' ') })
  }

  /** Strings that would be shown as they are: literals, both sides of a ?:, the right of && / || / ??, templates. */
  const checkShown = (node: ts.Node | undefined, test: (text: string) => boolean = isWords) => {
    if (!node) return
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
      if (test(node.text)) flag(node, node.text)
    } else if (ts.isTemplateExpression(node)) {
      const parts = [node.head.text, ...node.templateSpans.map((s) => s.literal.text)].join('{}')
      if (test(parts)) flag(node, parts)
      for (const span of node.templateSpans) checkShown(span.expression, test)
    } else if (ts.isParenthesizedExpression(node) || ts.isJsxExpression(node) || ts.isAsExpression(node)) {
      checkShown(node.expression, test)
    } else if (ts.isConditionalExpression(node)) {
      checkShown(node.whenTrue, test)
      checkShown(node.whenFalse, test)
    } else if (ts.isBinaryExpression(node)) {
      const op = node.operatorToken.kind
      if (op === ts.SyntaxKind.AmpersandAmpersandToken || op === ts.SyntaxKind.BarBarToken || op === ts.SyntaxKind.QuestionQuestionToken) {
        if (op !== ts.SyntaxKind.AmpersandAmpersandToken) checkShown(node.left, test)
        checkShown(node.right, test)
      } else if (op === ts.SyntaxKind.PlusToken) {
        checkShown(node.left, test)
        checkShown(node.right, test)
      }
    }
  }

  const visit = (node: ts.Node): void => {
    // Marked text.
    if (ts.isCallExpression(node)) {
      const name = calleeName(node.expression)
      const last = name?.split('.').pop()
      const viaTranslator = name?.includes('.') && (last === 't' || last === 'tn' || last === 'tc')
      if (name && (TRANSLATE.has(name) || viaTranslator)) {
        const [a, b, c] = node.arguments
        let entry: Marked | undefined
        if (last === 'tn') {
          const one = literal(b)
          const other = literal(c)
          if (one !== undefined && other !== undefined) entry = mark(node, other, { one })
        } else if (last === 'docText') {
          const english = literal(b)
          if (english !== undefined) entry = mark(node, english)
        } else if (last === 'tc') {
          const context = literal(a)
          const english = literal(b)
          if (context !== undefined && english !== undefined) entry = mark(node, english, { context })
        } else {
          const english = literal(a)
          if (english !== undefined) entry = mark(node, english)
        }
        if (entry && name !== 'msg' && name !== 'docText' && !insideFunction(node)) result.moduleLevel.push(entry)
        // The values may hold more marked text: t('{x} left', { x: t('Two') }).
        node.arguments.forEach((arg) => visit(arg))
        return
      }
      if (name && (TOAST.test(name) || TEXT_FUNCTIONS.has(name))) node.arguments.forEach((arg) => checkShown(arg))
      if (name === 'cn' || name?.startsWith('console.') || name === 'cva') return
    }

    if ((ts.isJsxSelfClosingElement(node) || ts.isJsxOpeningElement(node)) && ts.isIdentifier(node.tagName) && node.tagName.text in TEXT_COMPONENTS) {
      const prop = TEXT_COMPONENTS[node.tagName.text]
      for (const attr of node.attributes.properties) {
        if (ts.isJsxAttribute(attr) && attr.name.getText(sf) === prop) {
          const english = literal(attr.initializer)
          if (english !== undefined) mark(attr, english)
        }
      }
    }

    // Unmarked text.
    if (ts.isJsxText(node)) {
      if (isWords(node.text)) flag(node, node.text)
    } else if (ts.isJsxExpression(node) && node.parent && (ts.isJsxElement(node.parent) || ts.isJsxFragment(node.parent))) {
      checkShown(node.expression)
    } else if (ts.isJsxAttribute(node)) {
      const name = node.name.getText(sf)
      const component = ts.isJsxAttributes(node.parent) ? node.parent.parent.tagName.getText(sf) : ''
      if (TEXT_NAME.test(name) && TEXT_COMPONENTS[component] !== name) checkShown(node.initializer)
    } else if (ts.isPropertyAssignment(node)) {
      const name = node.name.getText(sf).replace(/['"]/g, '')
      if (TEXT_NAME.test(name)) checkShown(node.initializer)
      else if (ts.isObjectLiteralExpression(node.parent)) {
        const holder = node.parent.parent
        const holderName = holder && ts.isVariableDeclaration(holder) ? holder.name.getText(sf) : ''
        if (LABEL_OBJECT.test(holderName)) checkShown(node.initializer, isSentence)
      }
    } else if (ts.isReturnStatement(node)) {
      checkShown(node.expression, isSentence)
    } else if (ts.isArrowFunction(node) && !ts.isBlock(node.body)) {
      checkShown(node.body, isSentence)
    } else if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.EqualsToken && node.left.getText(sf) === 'document.title') {
      checkShown(node.right)
    }

    ts.forEachChild(node, visit)
  }

  visit(sf)
  return result
}

export function scan(root: string): ScanResult {
  const all: ScanResult = { marked: [], unmarked: [], moduleLevel: [] }
  for (const path of files(root)) {
    const r = scanFile(path, root)
    all.marked.push(...r.marked)
    all.unmarked.push(...r.unmarked)
    all.moduleLevel.push(...r.moduleLevel)
  }
  return all
}

export const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort()
