import { Activity, Database, IndianRupee, Sun } from 'lucide-react'
import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, Tooltip, XAxis, YAxis, type TooltipContentProps } from 'recharts'
import { useMotionLevel } from '@/components/fx/preferences-context'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { StatTile } from '@/features/dashboard/StatTile'
import { errorMessage } from '@/lib/api/errors'
import { formatDateParts, formatINR } from '@/lib/format'
import { msg, t, tn } from '@/lib/i18n'
import { cn } from '@/lib/utils'
import { featureLabel, featureLabels, useAiCalls, useAiQueries, useAiUsageSummary, type AiCall, type AiQuery, type AiQueryOutcome, type AiUsageSummary } from './api'

const ALL = 'all'
const periods = [7, 30, 90]
const tokens = new Intl.NumberFormat('en-IN')
const time = (iso: string) => formatDateParts(new Date(iso), { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Kolkata' })
const dayTick = (day: string) => formatDateParts(new Date(`${day}T00:00:00Z`), { day: 'numeric', month: 'short', timeZone: 'UTC' })
const seconds = (ms: number) => t('{seconds} s', { seconds: (ms / 1000).toFixed(1) })

const outcomeStyles: Record<string, string> = {
  Success: 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
  Succeeded: 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
  Refused: 'bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-200',
  Rejected: 'bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-200',
  Truncated: 'bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-200',
  Failed: 'bg-red-50 text-red-800 dark:bg-red-950 dark:text-red-200',
}

const outcomeLabels: Record<string, string> = {
  Success: msg('Success'),
  Succeeded: msg('Succeeded'),
  Refused: msg('Refused'),
  Rejected: msg('Rejected'),
  Truncated: msg('Truncated'),
  Failed: msg('Failed'),
}

function Outcome({ value }: { value: string }) {
  const label = outcomeLabels[value]
  return (
    <Badge variant="outline" className={cn('border-transparent', outcomeStyles[value])}>
      {label ? t(label) : value}
    </Badge>
  )
}

function DailyTooltip({ active, payload }: TooltipContentProps) {
  const day = active ? (payload?.[0]?.payload as AiUsageSummary['daily'][number] | undefined) : undefined
  if (!day) return null
  return (
    <div className="rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-md">
      <p className="text-sm font-semibold tabular-nums">{formatINR(day.costInr)}</p>
      <p className="text-muted-foreground">
        {dayTick(day.day)} · {tn(day.calls, '{count} call', '{count} calls')}
      </p>
    </div>
  )
}

function DailyChart({ daily }: { daily: AiUsageSummary['daily'] }) {
  const data = daily.map((d) => ({ ...d, tick: dayTick(d.day) }))
  const tick = { fill: 'var(--muted-foreground)', fontSize: 12 }
  const level = useMotionLevel()
  return (
    <figure aria-label={t('Estimated AI spend per day. Exact values are in the tooltips.')}>
      <BarChart data={data} responsive style={{ width: '100%', height: 220 }} margin={{ top: 8, right: 8, bottom: 0, left: 0 }} barCategoryGap="20%">
        <CartesianGrid vertical={false} stroke="var(--border)" />
        <XAxis dataKey="tick" tickLine={false} axisLine={{ stroke: 'var(--border)' }} tick={tick} interval="preserveStartEnd" minTickGap={16} />
        <YAxis tickFormatter={(v: number) => `₹${v}`} tickLine={false} axisLine={false} tick={tick} width={56} />
        <Tooltip content={DailyTooltip} cursor={{ fill: 'var(--muted)', opacity: 0.7 }} isAnimationActive={false} />
        <Bar dataKey="costInr" fill="var(--viz-series-1)" radius={[3, 3, 0, 0]} maxBarSize={28} isAnimationActive={level !== 'off'} animationDuration={900} />
      </BarChart>
    </figure>
  )
}

const callColumns: Column<AiCall>[] = [
  { key: 'time', header: msg('When'), cell: (c) => time(c.createdAtUtc), className: 'w-32 whitespace-nowrap' },
  { key: 'feature', header: msg('Feature'), cell: (c) => featureLabel(c.feature) },
  { key: 'user', header: msg('User'), cell: (c) => c.userName },
  {
    key: 'tokens',
    header: msg('Tokens in / cached / out'),
    align: 'right',
    cell: (c) => (
      <span className="tabular-nums">
        {tokens.format(c.inputTokens + c.cacheWriteTokens)} / {tokens.format(c.cacheReadTokens)} / {tokens.format(c.outputTokens)}
      </span>
    ),
  },
  { key: 'time-taken', header: msg('Took'), align: 'right', cell: (c) => seconds(c.durationMs) },
  { key: 'cost', header: msg('Cost'), align: 'right', cell: (c) => formatINR(c.costInr) },
  {
    key: 'outcome',
    header: msg('Outcome'),
    cell: (c) => (
      <span className="flex flex-col items-start gap-0.5">
        <Outcome value={c.outcome} />
        {c.error && <span className="max-w-64 text-xs text-muted-foreground">{c.error}</span>}
      </span>
    ),
  },
]

const queryColumns: Column<AiQuery>[] = [
  { key: 'time', header: msg('When'), cell: (q) => time(q.createdAtUtc), className: 'w-32 whitespace-nowrap' },
  { key: 'user', header: msg('User'), cell: (q) => q.userName },
  {
    key: 'question',
    header: msg('Question and SQL'),
    cell: (q) => (
      <div className="flex max-w-xl flex-col gap-1">
        <span className="font-medium">{q.question}</span>
        <details>
          <summary className="cursor-pointer text-xs text-muted-foreground">{t('SQL ({runAs})', { runAs: q.runAs })}</summary>
          <pre className="mt-1 overflow-x-auto rounded-md bg-muted p-2 font-mono text-[11px] whitespace-pre-wrap">{q.sql}</pre>
        </details>
        {q.error && <span className="text-xs text-amber-800 dark:text-amber-300">{q.error}</span>}
      </div>
    ),
  },
  { key: 'rows', header: msg('Rows'), align: 'right', cell: (q) => (q.outcome === 'Succeeded' ? tokens.format(q.rowCount) : '—') },
  { key: 'outcome', header: msg('Outcome'), cell: (q) => <Outcome value={q.outcome} /> },
]

function CallsTab() {
  const [page, setPage] = useState(1)
  const [feature, setFeature] = useState<string>()
  const [problemsOnly, setProblemsOnly] = useState(false)
  const calls = useAiCalls({ page, feature, problemsOnly })

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-4">
        <Select value={feature ?? ALL} onValueChange={(v) => { setFeature(v === ALL ? undefined : v); setPage(1) }}>
          <SelectTrigger className="w-48" aria-label={t('Filter by feature')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t('All features')}</SelectItem>
            {Object.entries(featureLabels).map(([value, label]) => (
              <SelectItem key={value} value={value}>
                {t(label)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Switch id="problems" checked={problemsOnly} onCheckedChange={(v) => { setProblemsOnly(v); setPage(1) }} />
          <Label htmlFor="problems">{t('Only refused, cut-off or failed calls')}</Label>
        </div>
      </div>
      <DataTable columns={callColumns} rows={calls.data?.items} getRowKey={(c) => c.id} isLoading={calls.isPending} error={calls.error}
        emptyTitle={t('No AI calls')} emptyDescription={t('Calls to Claude appear here as people use the AI features.')} />
      {calls.data && (
        <Pagination page={calls.data.page} pageSize={calls.data.pageSize} totalCount={calls.data.totalCount} totalPages={calls.data.totalPages ?? 0} onPageChange={setPage} />
      )}
    </div>
  )
}

function QueriesTab() {
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  const [outcome, setOutcome] = useState<AiQueryOutcome>()
  const queries = useAiQueries({ page, q: q || undefined, outcome })

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={q} onChange={(value) => { setQ(value); setPage(1) }} placeholder={t('Search questions and SQL')} />
        <Select value={outcome ?? ALL} onValueChange={(v) => { setOutcome(v === ALL ? undefined : (v as AiQueryOutcome)); setPage(1) }}>
          <SelectTrigger className="w-44" aria-label={t('Filter by outcome')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t('All outcomes')}</SelectItem>
            <SelectItem value="Succeeded">{t('Succeeded')}</SelectItem>
            <SelectItem value="Rejected">{t('Refused by the checker')}</SelectItem>
            <SelectItem value="Failed">{t('Failed in the database')}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <DataTable columns={queryColumns} rows={queries.data?.items} getRowKey={(item) => item.id} isLoading={queries.isPending} error={queries.error}
        emptyTitle={t('No AI queries')} emptyDescription={t('SQL written by Ask your data and the assistant appears here, including anything the checker refused.')} />
      {queries.data && (
        <Pagination page={queries.data.page} pageSize={queries.data.pageSize} totalCount={queries.data.totalCount} totalPages={queries.data.totalPages ?? 0} onPageChange={setPage} />
      )}
    </div>
  )
}

export function AiUsagePage() {
  const [days, setDays] = useState(30)
  const summary = useAiUsageSummary(days)
  const data = summary.data

  return (
    <>
      <PageHeader
        title={t('AI usage')}
        description={t("What the AI features cost and how they're used. Costs are estimates from token counts and the configured price list; Anthropic's console has the actual bill. Conversations stay private to the people who had them.")}
        actions={
          <Select value={String(days)} onValueChange={(v) => setDays(Number(v))}>
            <SelectTrigger className="w-40" aria-label={t('Period')}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {periods.map((p) => (
                <SelectItem key={p} value={String(p)}>
                  {t('Last {days} days', { days: p })}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        }
      />

      {summary.isError && <p className="text-sm text-destructive">{errorMessage(summary.error)}</p>}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile label={t('Spend, last {days} days', { days })} icon={IndianRupee} value={data && formatINR(data.costInr)} isLoading={summary.isPending}
          hint={data && t('About ${amount}', { amount: data.costUsd.toFixed(2) })} />
        <StatTile label={t('Spend today')} icon={Sun} value={data && formatINR(data.todayCostInr)} isLoading={summary.isPending}
          hint={data && t("Each person's limit: ${amount} a day", { amount: data.dailyBudgetUsdPerUser })} />
        <StatTile label={t('Calls to Claude')} icon={Activity} value={data && tokens.format(data.calls)} isLoading={summary.isPending}
          hint={data && t('{failed} failed, {refused} refused, {truncated} cut off', { failed: data.failed, refused: data.refused, truncated: data.truncated })} />
        <StatTile label={t('Input read from cache')} icon={Database} value={data && `${data.cacheReadPercent}%`} isLoading={summary.isPending}
          hint={t('Cached input costs a twentieth of the normal price')} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('Spend per day')}</CardTitle>
          <CardDescription>{t('India time.')}</CardDescription>
        </CardHeader>
        <CardContent>{data ? <DailyChart daily={data.daily} /> : <Skeleton className="h-56 w-full" />}</CardContent>
      </Card>

      {data && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Card className="min-w-0">
            <CardHeader>
              <CardTitle className="text-base">{t('By feature')}</CardTitle>
            </CardHeader>
            <CardContent className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('Feature')}</TableHead>
                    <TableHead className="text-right">{t('Calls')}</TableHead>
                    <TableHead className="text-right">{t('Cost')}</TableHead>
                    <TableHead className="text-right">{t('Per call')}</TableHead>
                    <TableHead className="text-right">{t('Typical time')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.byFeature.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={5} className="text-muted-foreground">{t('No AI calls in this period.')}</TableCell>
                    </TableRow>
                  )}
                  {data.byFeature.map((f) => (
                    <TableRow key={f.feature}>
                      <TableCell className="font-medium">
                        {featureLabel(f.feature)}
                        {f.failed > 0 && <span className="block text-xs font-normal text-amber-700 dark:text-amber-300">{t('{count} not successful', { count: f.failed })}</span>}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{tokens.format(f.calls)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(f.costInr)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(f.averageCostInr)}</TableCell>
                      <TableCell className="text-right tabular-nums">{seconds(f.averageDurationMs)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <Card className="min-w-0">
            <CardHeader>
              <CardTitle className="text-base">{t('By person')}</CardTitle>
              <CardDescription>{t("The last 24 hours against each person's daily limit of ${amount}.", { amount: data.dailyBudgetUsdPerUser })}</CardDescription>
            </CardHeader>
            <CardContent className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('Person')}</TableHead>
                    <TableHead className="text-right">{t('Calls')}</TableHead>
                    <TableHead className="text-right">{t('Cost')}</TableHead>
                    <TableHead className="w-40">{t('Last 24 hours')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.byUser.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={4} className="text-muted-foreground">{t('No AI calls in this period.')}</TableCell>
                    </TableRow>
                  )}
                  {data.byUser.map((u) => (
                    <TableRow key={u.userId ?? 'background'}>
                      <TableCell className="font-medium">{u.name}</TableCell>
                      <TableCell className="text-right tabular-nums">{tokens.format(u.calls)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatINR(u.costInr)}</TableCell>
                      <TableCell>
                        <div className="flex items-center gap-2">
                          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted" role="presentation">
                            <div
                              className={cn('h-full rounded-full', u.budgetUsedPercent >= 80 ? 'bg-amber-500' : 'bg-viz-series-1')}
                              style={{ width: `${Math.min(100, u.budgetUsedPercent)}%` }}
                            />
                          </div>
                          <span className="w-20 text-right text-xs tabular-nums text-muted-foreground">
                            {formatINR(u.last24HoursInr)} · {u.budgetUsedPercent}%
                          </span>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{t('Activity')}</CardTitle>
          <CardDescription>{t('Every call to Claude, and every SQL query the AI wrote with what the checker or the database said about it.')}</CardDescription>
        </CardHeader>
        <CardContent>
          <Tabs defaultValue="calls">
            <TabsList>
              <TabsTrigger value="calls">{t('Calls to Claude')}</TabsTrigger>
              <TabsTrigger value="queries">{t('SQL queries')}</TabsTrigger>
            </TabsList>
            <TabsContent value="calls" className="pt-2">
              <CallsTab />
            </TabsContent>
            <TabsContent value="queries" className="pt-2">
              <QueriesTab />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>
    </>
  )
}

