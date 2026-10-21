import { Download, Eraser, Pencil, Percent, Save, Trash2, TriangleAlert, Upload } from 'lucide-react'
import { useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { errorMessage } from '@/lib/api/errors'
import { formatBusinessDate, formatDate, formatINR, formatPercent, todayInIndia } from '@/lib/format'
import { useCategories } from '@/lib/lookups'
import { cn } from '@/lib/utils'
import { downloadRateSheet, useDeleteRate, useGridProducts, useImportRates, useRates, useSaveRates, useUpdateRate, type ProductRate } from './api'
import { applyPercent, monthPeriod, parseRate, type Rounding } from './rate-math'
import { msg, t, tn } from '@/lib/i18n'

const ALL = 'all'
const isDate = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value)

function describePeriod(from: string, to: string | null | undefined) {
  return to ? t('{from} to {to}', { from: formatBusinessDate(from), to: formatBusinessDate(to) }) : t('{from} onwards', { from: formatBusinessDate(from) })
}

/** Set the rates for one period: type them, change all by a percentage, or fill them from an Excel sheet. */
function NewRatesTab() {
  const today = todayInIndia()
  const nextMonth = monthPeriod(today, 1)
  const [from, setFrom] = useState(nextMonth.from)
  const [to, setTo] = useState(nextMonth.to)
  const [note, setNote] = useState('')
  const [q, setQ] = useState('')
  const [categoryId, setCategoryId] = useState<number | undefined>()
  const [rates, setRates] = useState<Record<number, string>>({})
  const [percent, setPercent] = useState('')
  const [rounding, setRounding] = useState<Rounding>('rupee')
  const [problems, setProblems] = useState<string[]>([])
  const [confirming, setConfirming] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)

  const categories = useCategories()
  const products = useGridProducts(from, categoryId, q)
  const save = useSaveRates()
  const importSheet = useImportRates()

  const shown = products.data ?? []
  const entries = Object.entries(rates).filter(([, text]) => text.trim() !== '')
  const invalid = entries.filter(([, text]) => parseRate(text) === null)
  const shownIds = new Set(shown.map((p) => p.id))
  const hidden = entries.filter(([id]) => !shownIds.has(Number(id))).length
  const periodProblem = !isDate(from)
    ? t('Choose the first day the rates apply.')
    : to && !isDate(to)
      ? t('Enter the last day as a full date, or leave it empty.')
      : to && to < from
        ? t("The last day can't be before the first day.")
        : null
  const canSave = entries.length > 0 && invalid.length === 0 && !periodProblem && !save.isPending

  function applyToShown() {
    const value = Number(percent)
    if (!percent.trim() || Number.isNaN(value)) {
      toast.error(t('Enter the change as a percentage, like 5 or -2.5.'))
      return
    }
    setRates((current) => {
      const next = { ...current }
      for (const product of shown) next[product.id] = applyPercent(product.price, value, rounding)
      return next
    })
    toast.success(t('New rates filled in for {count} products', { count: shown.length }), { description: t('Review them, then save.') })
  }

  async function upload(file: File) {
    try {
      const result = await importSheet.mutateAsync(file)
      setRates((current) => {
        const next = { ...current }
        for (const line of result.lines) next[line.productId] = line.rate.toFixed(2)
        return next
      })
      setProblems(result.problems)
      toast.success(t('Read {count} rates from the sheet', { count: result.lines.length }), {
        description: result.problems.length > 0 ? t('{count} rows were skipped; see the list.', { count: result.problems.length }) : t('Review them, then save.'),
      })
    } catch (error) {
      toast.error(errorMessage(error))
    } finally {
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  async function confirmSave() {
    setConfirming(false)
    try {
      const result = await save.mutateAsync({
        effectiveFrom: from,
        effectiveTo: to || null,
        note: note.trim() || null,
        lines: entries.map(([id, text]) => ({ productId: Number(id), rate: parseRate(text)! })),
      })
      toast.success(t('Rates saved for {period}', { period: describePeriod(from, to) }), {
        description: [result.created > 0 && t('{count} new', { count: result.created }), result.updated > 0 && t('{count} changed', { count: result.updated })]
          .filter(Boolean)
          .join(', '),
      })
      setRates({})
      setProblems([])
    } catch (error) {
      toast.error(errorMessage(error), { duration: 15_000 })
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>{t('Period')}</CardTitle>
          <CardDescription>
            {t('Orders dated in this period use these rates. Leave the last day empty for rates that run until a later period replaces them.')}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap items-end gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="rate-from">{t('First day')}</Label>
            <Input id="rate-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="w-44" />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="rate-to">{t('Last day')}</Label>
            <Input id="rate-to" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} className="w-44" />
          </div>
          <div className="flex gap-2">
            {[
              { label: t('This month'), period: monthPeriod(today) },
              { label: t('Next month'), period: nextMonth },
            ].map(({ label, period }) => (
              <Button
                key={label}
                type="button"
                variant="outline"
                size="sm"
                onClick={() => {
                  setFrom(period.from)
                  setTo(period.to)
                }}
              >
                {label}
              </Button>
            ))}
          </div>
          <div className="flex min-w-60 flex-1 flex-col gap-1.5">
            <Label htmlFor="rate-note">{t('Note')}</Label>
            <Input id="rate-note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} placeholder={t('Optional, e.g. Cotton price rise')} />
          </div>
          {periodProblem && <p className="w-full text-sm text-destructive">{periodProblem}</p>}
        </CardContent>
      </Card>

      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={q} onChange={setQ} placeholder={t('Search SKU or name')} />
        <Select value={categoryId?.toString() ?? ALL} onValueChange={(value) => setCategoryId(value === ALL ? undefined : Number(value))}>
          <SelectTrigger className="w-52" aria-label={t('Filter by category')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t('All categories')}</SelectItem>
            {categories.data?.map((c) => (
              <SelectItem key={c.id} value={c.id.toString()}>
                {c.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Input
            aria-label={t('Percentage change')}
            inputMode="decimal"
            value={percent}
            onChange={(e) => setPercent(e.target.value)}
            placeholder="+5"
            className="w-20"
          />
          <Select value={rounding} onValueChange={(value) => setRounding(value as Rounding)}>
            <SelectTrigger className="w-40" aria-label={t('Rounding')}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="rupee">{t('Whole rupees')}</SelectItem>
              <SelectItem value="paisa">{t('To the paisa')}</SelectItem>
            </SelectContent>
          </Select>
          <Button type="button" variant="outline" onClick={applyToShown} disabled={shown.length === 0}>
            <Percent />
            {t('Change all shown')}
          </Button>
        </div>
        <div className="ml-auto flex gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={!isDate(from)}
            onClick={() => void downloadRateSheet({ on: from, categoryId, q: q || undefined }).catch((e: unknown) => toast.error(errorMessage(e)))}
          >
            <Download />
            {t('Download sheet')}
          </Button>
          <Button type="button" variant="outline" disabled={importSheet.isPending} onClick={() => fileInput.current?.click()}>
            {importSheet.isPending ? <Spinner /> : <Upload />}
            {t('Upload sheet')}
          </Button>
          <input
            ref={fileInput}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) void upload(file)
            }}
          />
        </div>
      </div>

      {problems.length > 0 && (
        <Alert>
          <TriangleAlert />
          <AlertTitle>{t('Some rows of the sheet were skipped')}</AlertTitle>
          <AlertDescription>
            <ul className="list-disc pl-4">
              {problems.slice(0, 20).map((p) => (
                <li key={p}>{p}</li>
              ))}
              {problems.length > 20 && <li>{t('and {count} more', { count: problems.length - 20 })}</li>}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      <div className="overflow-x-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-36">SKU</TableHead>
              <TableHead>{t('Product')}</TableHead>
              <TableHead className="w-20">{t('Unit')}</TableHead>
              <TableHead className="w-40 text-right">{isDate(from) ? t('Price on {date}', { date: formatBusinessDate(from) }) : t('Price on the first day')}</TableHead>
              <TableHead className="w-36">{t('New rate (₹)')}</TableHead>
              <TableHead className="w-24 text-right">{t('Change')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {products.isPending && (
              <TableRow>
                <TableCell colSpan={6} className="py-8 text-center text-muted-foreground">
                  <Spinner className="mx-auto" />
                </TableCell>
              </TableRow>
            )}
            {!products.isPending && shown.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="py-8 text-center text-muted-foreground">
                  {t('No active products match.')}
                </TableCell>
              </TableRow>
            )}
            {shown.map((product) => {
              const text = rates[product.id] ?? ''
              const value = parseRate(text)
              const bad = text.trim() !== '' && value === null
              const change = value !== null && product.price > 0 ? ((value - product.price) / product.price) * 100 : null
              return (
                <TableRow key={product.id}>
                  <TableCell className="font-mono text-xs">{product.sku}</TableCell>
                  <TableCell className="font-medium whitespace-normal">{product.name}</TableCell>
                  <TableCell>{product.unit}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    <span className="inline-flex items-center gap-1.5">
                      {product.hasRate && (
                        <Badge variant="outline" title={t('From a rate list period')}>
                          {t('Rate list')}
                        </Badge>
                      )}
                      {formatINR(product.price)}
                    </span>
                  </TableCell>
                  <TableCell>
                    <Input
                      aria-label={t('New rate for {name}', { name: product.name })}
                      aria-invalid={bad}
                      inputMode="decimal"
                      value={text}
                      placeholder="—"
                      onChange={(e) => setRates((current) => ({ ...current, [product.id]: e.target.value }))}
                      className="h-8 text-right tabular-nums"
                    />
                  </TableCell>
                  <TableCell
                    className={cn(
                      'text-right text-sm tabular-nums',
                      change !== null && change > 0 && 'text-viz-up',
                      change !== null && change < 0 && 'text-destructive',
                    )}
                  >
                    {change === null ? '' : `${change > 0 ? '+' : ''}${formatPercent(Math.round(change * 10) / 10)}`}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </div>

      <div className="sticky bottom-0 -mx-4 flex flex-wrap items-center justify-end gap-3 border-t bg-background/95 px-4 py-3 backdrop-blur md:-mx-6 md:px-6">
        <span className="mr-auto text-sm text-muted-foreground">
          {entries.length === 0
            ? t('Type new rates, change all shown by a percentage, or upload a filled-in sheet.')
            : [
                tn(entries.length, '{count} new rate', '{count} new rates'),
                hidden > 0 ? ` ${t('({count} not shown by the current filter)', { count: hidden })}` : '',
                invalid.length > 0 ? `; ${tn(invalid.length, "{count} isn't a valid rate", "{count} aren't valid rates")}` : '',
                '.',
              ].join('')}
        </span>
        <Button type="button" variant="ghost" disabled={entries.length === 0} onClick={() => setRates({})}>
          <Eraser />
          {t('Clear')}
        </Button>
        <Button type="button" disabled={!canSave} onClick={() => setConfirming(true)}>
          {save.isPending ? <Spinner /> : <Save />}
          {t('Save rates')}
        </Button>
      </div>

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={tn(entries.length, 'Save {count} rate?', 'Save {count} rates?')}
        description={t('They apply to orders dated {period}. Orders already saved keep their prices.', { period: describePeriod(from, to) })}
        confirmLabel={t('Save rates')}
        onConfirm={() => void confirmSave()}
      />
    </div>
  )
}

/** Changes one rate: its amount, its period or its note. */
function EditRateDialog({ rate, onClose }: { rate: ProductRate | null; onClose: () => void }) {
  return (
    <Dialog open={rate !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent>{rate && <EditRateForm rate={rate} onDone={onClose} />}</DialogContent>
    </Dialog>
  )
}

function EditRateForm({ rate, onDone }: { rate: ProductRate; onDone: () => void }) {
  const update = useUpdateRate()
  const [amount, setAmount] = useState(rate.rate.toFixed(2))
  const [from, setFrom] = useState(rate.effectiveFrom)
  const [to, setTo] = useState(rate.effectiveTo ?? '')
  const [note, setNote] = useState(rate.note ?? '')
  const value = parseRate(amount)

  async function submit() {
    if (value === null || !isDate(from)) return
    try {
      await update.mutateAsync({ id: rate.id, body: { rate: value, effectiveFrom: from, effectiveTo: to || null, note: note.trim() || null } })
      toast.success(t('Rate for {name} updated', { name: rate.productName }))
      onDone()
    } catch (error) {
      toast.error(errorMessage(error))
    }
  }

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        void submit()
      }}
      noValidate
    >
      <DialogHeader>
        <DialogTitle>{rate.productName}</DialogTitle>
        <DialogDescription>
          {t('{sku}. Orders already saved keep their prices; the change applies to orders priced from now on.', { sku: rate.sku })}
        </DialogDescription>
      </DialogHeader>
      <div className="grid gap-4 py-4 sm:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="edit-rate">{t('Rate (₹)')}</Label>
          <Input id="edit-rate" inputMode="decimal" value={amount} aria-invalid={value === null} onChange={(e) => setAmount(e.target.value)} autoFocus />
        </div>
        <div />
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="edit-from">{t('First day')}</Label>
          <Input id="edit-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="edit-to">{t('Last day')}</Label>
          <Input id="edit-to" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1.5 sm:col-span-2">
          <Label htmlFor="edit-note">{t('Note')}</Label>
          <Input id="edit-note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
        </div>
      </div>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={value === null || !isDate(from) || update.isPending}>
          {update.isPending && <Spinner />}
          {t('Save')}
        </Button>
      </DialogFooter>
    </form>
  )
}

/** Every rate period, newest first, with who set it. */
function HistoryTab({ canEdit }: { canEdit: boolean }) {
  const [params, update] = useListParams()
  const activeOn = params.filters.activeOn
  const rates = useRates({ q: params.q || undefined, page: params.page, pageSize: params.pageSize, activeOn: activeOn || undefined })
  const remove = useDeleteRate()
  const [editing, setEditing] = useState<ProductRate | null>(null)
  const [deleting, setDeleting] = useState<ProductRate | null>(null)

  const columns: Column<ProductRate>[] = [
    {
      key: 'product',
      header: msg('Product'),
      cell: (r) => (
        <div className="flex flex-col">
          <span className="font-medium">{r.productName}</span>
          <span className="font-mono text-xs text-muted-foreground">{r.sku}</span>
        </div>
      ),
    },
    { key: 'rate', header: msg('Rate'), align: 'right', cell: (r) => `${formatINR(r.rate)} / ${r.unit.toLowerCase()}` },
    { key: 'period', header: msg('Period'), cell: (r) => describePeriod(r.effectiveFrom, r.effectiveTo) },
    { key: 'note', header: msg('Note'), cell: (r) => r.note ?? '—', className: 'whitespace-normal' },
    { key: 'by', header: msg('Set by'), cell: (r) => (r.updatedBy ? `${r.updatedBy}, ${formatDate(r.updatedAtUtc)}` : formatDate(r.updatedAtUtc)) },
  ]
  if (canEdit)
    columns.push({
      key: 'actions',
      header: '',
      className: 'w-24',
      cell: (r) => (
        <div className="flex justify-end gap-1">
          <Button variant="ghost" size="icon-sm" onClick={() => setEditing(r)} aria-label={t('Edit the rate for {name}', { name: r.productName })}>
            <Pencil />
          </Button>
          <Button variant="ghost" size="icon-sm" onClick={() => setDeleting(r)} aria-label={t('Remove the rate for {name}', { name: r.productName })}>
            <Trash2 />
          </Button>
        </div>
      ),
    })

  async function confirmDelete() {
    if (!deleting) return
    try {
      await remove.mutateAsync(deleting.id)
      toast.success(t('Rate for {name} removed', { name: deleting.productName }))
    } catch (error) {
      toast.error(errorMessage(error))
    } finally {
      setDeleting(null)
    }
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder={t('Search SKU or name')} />
        <div className="flex items-center gap-2">
          <Label htmlFor="active-on">{t('In force on')}</Label>
          <Input
            id="active-on"
            type="date"
            value={activeOn ?? ''}
            onChange={(e) => update({ filters: { activeOn: e.target.value || undefined } })}
            className="w-44"
          />
        </div>
      </div>
      <DataTable
        columns={columns}
        rows={rates.data?.items}
        getRowKey={(r) => r.id}
        isLoading={rates.isPending}
        error={rates.error}
        emptyTitle={t('No rates yet')}
        emptyDescription={canEdit ? t('Set a period’s rates on the New rates tab.') : undefined}
      />
      {rates.data && (
        <Pagination
          page={rates.data.page}
          pageSize={rates.data.pageSize}
          totalCount={rates.data.totalCount}
          totalPages={rates.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}
      <EditRateDialog rate={editing} onClose={() => setEditing(null)} />
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t('Remove this rate?')}
        description={
          deleting
            ? t('{name}: {rate} for {period}. Orders in that period will then use the list price. Orders already saved keep their prices.', {
                name: deleting.productName,
                rate: formatINR(deleting.rate),
                period: describePeriod(deleting.effectiveFrom, deleting.effectiveTo),
              })
            : ''
        }
        confirmLabel={t('Remove')}
        destructive
        onConfirm={() => void confirmDelete()}
      />
    </>
  )
}

export function RatesPage() {
  const { can } = useAuth()
  const canEdit = can(Permissions.RatesEdit)
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = canEdit && searchParams.get('tab') !== 'history' ? 'new' : 'history'

  return (
    <>
      <PageHeader
        title={t('Rate updates')}
        description={t("Set each product's rate for a period, such as a month. An order uses the rate for its order date; outside every period it uses the product's list price. Orders already saved keep their prices.")}
      />
      <Tabs value={tab} onValueChange={(next) => setSearchParams(next === 'history' ? { tab: next } : {})}>
        <TabsList>
          {canEdit && <TabsTrigger value="new">{t('New rates')}</TabsTrigger>}
          <TabsTrigger value="history">{t('Rate history')}</TabsTrigger>
        </TabsList>
        {canEdit && (
          <TabsContent value="new" className="pt-2">
            {tab === 'new' && <NewRatesTab />}
          </TabsContent>
        )}
        <TabsContent value="history" className="flex flex-col gap-4 pt-2">
          {tab === 'history' && <HistoryTab canEdit={canEdit} />}
        </TabsContent>
      </Tabs>
    </>
  )
}
