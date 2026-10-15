import { ArrowLeft, Download } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { errorMessage } from '@/lib/api/errors'
import { todayInIndia } from '@/lib/format'
import { cn } from '@/lib/utils'
import { downloadReportExcel, useReport, useReportDefinitions, type ReportQuery, type ReportResult } from './api'
import { presetRange, rangePresets, type RangePreset } from './date-ranges'
import { columnHeader, formatCell, isNumeric } from './report-format'

const DEFAULT_PRESET: RangePreset = 'this-fy'

/** Report filters live in the URL, so a filtered report can be bookmarked or shared. */
function useReportQuery(usesDateRange: boolean, supportsPeriod: boolean) {
  const [searchParams, setSearchParams] = useSearchParams()
  const today = todayInIndia()
  const preset = (rangePresets.some((p) => p.value === searchParams.get('range')) ? searchParams.get('range') : DEFAULT_PRESET) as RangePreset
  const fallback = presetRange('this-fy', today)
  const range =
    preset === 'custom' ? { from: searchParams.get('from') || fallback.from, to: searchParams.get('to') || fallback.to } : presetRange(preset, today)
  const period = searchParams.get('period') === 'day' ? 'day' : 'month'

  const query: ReportQuery = {
    ...(usesDateRange ? range : {}),
    ...(supportsPeriod ? { period } : {}),
  }

  const set = (changes: Record<string, string | undefined>) =>
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current)
        for (const [key, value] of Object.entries(changes)) {
          if (value) next.set(key, value)
          else next.delete(key)
        }
        return next
      },
      { replace: true },
    )

  return { preset, range, period, query, set }
}

function ReportTable({ report, dimmed }: { report: ReportResult; dimmed: boolean }) {
  if (report.rows.length === 0) {
    return (
      <Empty className="border">
        <EmptyHeader>
          <EmptyTitle>Nothing to report</EmptyTitle>
          <EmptyDescription>There's no data for this period. Try a wider date range.</EmptyDescription>
        </EmptyHeader>
      </Empty>
    )
  }

  const align = (type: ReportResult['columns'][number]['type']) => (isNumeric(type) ? 'text-right tabular-nums' : undefined)

  return (
    <div className={cn('rounded-lg border transition-opacity', dimmed && 'opacity-60')}>
      <Table>
        <TableHeader className="bg-muted/50">
          <TableRow>
            {report.columns.map((c) => (
              <TableHead key={c.key} className={align(c.type)}>
                {columnHeader(c)}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {report.rows.map((row, i) => (
            <TableRow key={i}>
              {report.columns.map((c) => (
                <TableCell key={c.key} className={align(c.type)}>
                  {formatCell(row[c.key], c.type)}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
        <TableFooter>
          <TableRow className="font-semibold">
            {report.columns.map((c) => (
              <TableCell key={c.key} className={align(c.type)}>
                {formatCell(report.totals[c.key], c.type)}
              </TableCell>
            ))}
          </TableRow>
        </TableFooter>
      </Table>
    </div>
  )
}

export function ReportPage() {
  const { key = '' } = useParams()
  const definitions = useReportDefinitions()
  const definition = definitions.data?.find((d) => d.key === key)
  const usesDateRange = definition?.usesDateRange ?? false
  const supportsPeriod = key === 'sales-by-period'
  const { preset, range, period, query, set } = useReportQuery(usesDateRange, supportsPeriod)
  // Wait for the definition, so a dated report isn't fetched once without its range first.
  const report = useReport(key, query, definition !== undefined)
  const [downloading, setDownloading] = useState(false)

  if (definitions.data && !definition) {
    return (
      <Alert variant="destructive">
        <AlertDescription>
          There's no report called "{key}". <Link to="/reports" className="underline">See all reports</Link>.
        </AlertDescription>
      </Alert>
    )
  }

  async function download() {
    setDownloading(true)
    try {
      await downloadReportExcel(key, query)
    } catch (error) {
      toast.error(errorMessage(error))
    } finally {
      setDownloading(false)
    }
  }

  const today = todayInIndia()

  return (
    <>
      <PageHeader
        title={definition?.title ?? 'Report'}
        description={report.data?.subtitle ?? definition?.description}
        actions={
          <>
            <Button asChild variant="ghost">
              <Link to="/reports">
                <ArrowLeft /> All reports
              </Link>
            </Button>
            <Button onClick={download} disabled={downloading || !report.data}>
              {downloading ? <Spinner /> : <Download />} Download Excel
            </Button>
          </>
        }
      />

      {(usesDateRange || supportsPeriod) && (
        <div className="flex flex-wrap items-center gap-3">
          {usesDateRange && (
            <Select
              value={preset}
              onValueChange={(value) =>
                set(value === 'custom' ? { range: value, from: range.from, to: range.to } : { range: value, from: undefined, to: undefined })
              }
            >
              <SelectTrigger className="w-52" aria-label="Date range">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {rangePresets.map((p) => (
                  <SelectItem key={p.value} value={p.value}>
                    {p.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
          {usesDateRange && preset === 'custom' && (
            <div className="flex items-center gap-2">
              <Label htmlFor="report-from" className="text-muted-foreground">
                From
              </Label>
              <Input
                id="report-from"
                type="date"
                className="w-40"
                value={range.from}
                max={range.to}
                onChange={(e) => e.target.value && set({ from: e.target.value })}
              />
              <Label htmlFor="report-to" className="text-muted-foreground">
                to
              </Label>
              <Input
                id="report-to"
                type="date"
                className="w-40"
                value={range.to}
                min={range.from}
                max={today}
                onChange={(e) => e.target.value && set({ to: e.target.value })}
              />
            </div>
          )}
          {supportsPeriod && (
            <Select value={period} onValueChange={(value) => set({ period: value === 'month' ? undefined : value })}>
              <SelectTrigger className="w-36" aria-label="Group by">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="month">By month</SelectItem>
                <SelectItem value="day">By day</SelectItem>
              </SelectContent>
            </Select>
          )}
        </div>
      )}

      {report.error ? (
        <Alert variant="destructive">
          <AlertDescription>{errorMessage(report.error)}</AlertDescription>
        </Alert>
      ) : report.data ? (
        <ReportTable report={report.data} dimmed={report.isPlaceholderData} />
      ) : (
        <Skeleton className="h-96 w-full rounded-lg" />
      )}
    </>
  )
}
