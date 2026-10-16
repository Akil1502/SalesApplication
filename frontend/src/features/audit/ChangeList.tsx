import type { AuditEntry } from './api'

/** A record's changed fields, old value struck through beside the new one. */
export function ChangeList({ entry }: { entry: Pick<AuditEntry, 'changes' | 'summary'> }) {
  if (entry.changes.length === 0) return entry.summary ? <p className="text-sm text-muted-foreground">{entry.summary}</p> : null
  return (
    <dl className="grid grid-cols-[minmax(8rem,auto)_1fr] gap-x-4 gap-y-1 text-sm">
      {entry.changes.map((change, i) => (
        <div key={`${change.field}-${i}`} className="contents">
          <dt className="text-muted-foreground">{change.field}</dt>
          <dd className="break-words">
            {change.old !== null && change.old !== undefined && (
              <>
                <del className="text-muted-foreground decoration-muted-foreground/60">{change.old}</del>
                <span className="px-1.5 text-muted-foreground" aria-hidden>
                  →
                </span>
                <span className="sr-only">changed to</span>
              </>
            )}
            {change.new ?? <span className="text-muted-foreground italic">empty</span>}
          </dd>
        </div>
      ))}
    </dl>
  )
}
