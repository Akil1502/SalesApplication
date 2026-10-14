import Markdown, { type Components } from 'react-markdown'
import { Link } from 'react-router'
import remarkGfm from 'remark-gfm'

/**
 * Claude's replies as formatted text (bold, lists, tables). Deliberately limited, because a reply can quote text
 * that came from customers or uploaded documents: raw HTML is dropped, images are never loaded (an image URL is a
 * classic way to leak data to another server), and only links to pages inside the app are clickable.
 */
const components: Components = {
  a: ({ href, children }) =>
    href?.startsWith('/') && !href.startsWith('//') ? (
      <Link to={href} className="font-medium underline underline-offset-4">
        {children}
      </Link>
    ) : (
      <span>{children}</span>
    ),
  img: ({ alt }) => (alt ? <span className="text-muted-foreground">[{alt}]</span> : null),
  p: ({ children }) => <p className="leading-relaxed">{children}</p>,
  ul: ({ children }) => <ul className="ml-5 list-disc space-y-1">{children}</ul>,
  ol: ({ children }) => <ol className="ml-5 list-decimal space-y-1">{children}</ol>,
  h1: ({ children }) => <p className="font-semibold">{children}</p>,
  h2: ({ children }) => <p className="font-semibold">{children}</p>,
  h3: ({ children }) => <p className="font-semibold">{children}</p>,
  code: ({ children }) => <code className="rounded bg-muted px-1 py-0.5 font-mono text-xs">{children}</code>,
  pre: ({ children }) => <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs [&_code]:bg-transparent [&_code]:p-0">{children}</pre>,
  table: ({ children }) => (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-xs">{children}</table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-muted/60">{children}</thead>,
  th: ({ children, style }) => (
    <th className="px-2 py-1.5 text-left font-medium whitespace-nowrap" style={style}>
      {children}
    </th>
  ),
  td: ({ children, style }) => (
    <td className="border-t px-2 py-1.5 tabular-nums" style={style}>
      {children}
    </td>
  ),
}

export function MessageMarkdown({ text }: { text: string }) {
  return (
    <div className="flex flex-col gap-2 text-sm break-words">
      <Markdown remarkPlugins={[remarkGfm]} components={components} skipHtml>
        {text}
      </Markdown>
    </div>
  )
}
