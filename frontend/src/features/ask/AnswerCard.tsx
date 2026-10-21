import { Download, MessageSquareText } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api/client'
import { errorMessage } from '@/lib/api/errors'
import { formatINR } from '@/lib/format'
import { t, tn } from '@/lib/i18n'
import { cn } from '@/lib/utils'
import type { AskAnswer } from './api'
import { AnswerChart } from './AnswerChart'
import { askColumnHeader, formatAskValue, isNumericColumn } from './ask-format'

async function downloadExcel(resultId: string) {
  const { data, response } = await api.GET('/api/ask/results/{id}/excel', { params: { path: { id: resultId } }, parseAs: 'blob' })
  if (!response.ok || !data) throw new Error(response.status === 404 ? t('That answer has expired. Ask the question again to download it.') : t('The Excel file could not be created.'))
  const url = URL.createObjectURL(data as Blob)
  const link = document.createElement('a')
  link.href = url
  link.download = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? 'answer.xlsx'
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/** One answer: Claude's short summary, then the query's own rows as a chart and table, and the SQL that produced them. */
export function AnswerCard({ answer, onFollowUp }: { answer: AskAnswer; onFollowUp: (question: string) => void }) {
  const [downloading, setDownloading] = useState(false)
  const hasRows = answer.columns.length > 0

  async function download() {
    if (!answer.resultId) return
    setDownloading(true)
    try {
      await downloadExcel(answer.resultId)
    } catch (error) {
      toast.error(errorMessage(error))
    } finally {
      setDownloading(false)
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm leading-relaxed whitespace-pre-line">{answer.summary || t('Here is what the data shows.')}</p>

      {hasRows && (
        <>
          <AnswerChart answer={answer} />
          <div className="max-h-96 overflow-auto rounded-lg border">
            <Table>
              <TableHeader className="sticky top-0 z-10 bg-muted">
                <TableRow>
                  {answer.columns.map((c) => (
                    <TableHead key={c.name} className={cn(isNumericColumn(c) && 'text-right')}>
                      {askColumnHeader(c)}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {answer.rows.map((row, r) => (
                  <TableRow key={r}>
                    {answer.columns.map((c, i) => (
                      <TableCell key={c.name} className={cn(isNumericColumn(c) && 'text-right tabular-nums')}>
                        {formatAskValue(row[i], c)}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <p className="text-xs text-muted-foreground">
            {answer.truncated
              ? t('{count} rows (the query returned more; only the first 500 are shown). Straight from the database.', { count: answer.rows.length.toLocaleString('en-IN') })
              : tn(answer.rows.length, '{count} row. Straight from the database.', '{count} rows. Straight from the database.')}
          </p>
        </>
      )}

      {answer.sql && (
        <details open className="rounded-lg border bg-muted/40">
          <summary className="cursor-pointer px-3 py-2 text-xs font-medium text-muted-foreground">
            {answer.purpose ? t('SQL used: {purpose}', { purpose: answer.purpose }) : t('SQL used')}
          </summary>
          <pre className="max-h-48 overflow-auto px-3 pb-3 font-mono text-xs leading-relaxed whitespace-pre-wrap">{answer.sql}</pre>
        </details>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {answer.resultId && (
          <Button size="sm" variant="outline" onClick={() => void download()} disabled={downloading}>
            {downloading ? <Spinner /> : <Download />} Excel
          </Button>
        )}
        {answer.followUps.map((followUp) => (
          <Button key={followUp} size="sm" variant="ghost" className="h-auto py-1 text-left whitespace-normal" onClick={() => onFollowUp(followUp)}>
            <MessageSquareText /> {followUp}
          </Button>
        ))}
      </div>

      <p className="text-xs text-muted-foreground">
        {tn(answer.queriesRun, 'Answered in {seconds} s · {count} query · estimated cost {cost}', 'Answered in {seconds} s · {count} queries · estimated cost {cost}', {
          seconds: (answer.usage.durationMs / 1000).toFixed(1),
          cost: formatINR(answer.usage.costInr),
        })}
      </p>
    </div>
  )
}
