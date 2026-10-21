import { ArrowLeft, FileText, ImageIcon, Paperclip, Sparkles, X } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link } from 'react-router'
import { CustomerPicker, type PickedCustomer } from '@/components/CustomerPicker'
import { ScanOverlay } from '@/components/fx/ScanOverlay'
import { StillOrb } from '@/components/fx/StillOrb'
import { PageHeader } from '@/components/PageHeader'
import { AiOrb3D } from '@/components/three/scenes'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { api } from '@/lib/api/client'
import { errorMessage, unwrap } from '@/lib/api/errors'
import { t } from '@/lib/i18n'
import { useExtractOrder, type OrderExtraction } from './api'
import { prepareUpload } from './image'
import { ReviewOrder } from './ReviewOrder'

const ACCEPTED = 'application/pdf,image/jpeg,image/png,image/webp,image/gif'
const EXAMPLE = `Vanakkam sir, pls send
30s combed 500 kg
40s cc 10 bags
poplin 60x60 2000 mtrs
PO 4471, delivery by Friday. Murugan Garments, Tiruppur`

function formatSize(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function AiOff({ reason }: { reason?: string | null }) {
  const { can } = useAuth()
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <Sparkles />
        </EmptyMedia>
        <EmptyTitle>{t('Smart order entry is switched off')}</EmptyTitle>
        <EmptyDescription>
          {can(Permissions.SettingsEdit)
            ? t("{reason} Add a Claude API key to the server's user-secrets (see the README), then restart the API.", { reason: reason ?? '' })
            : t('Ask your administrator to set up the AI features. You can still enter orders by hand.')}
        </EmptyDescription>
      </EmptyHeader>
      <Button asChild variant="outline">
        <Link to="/orders/new">{t('Enter an order by hand')}</Link>
      </Button>
    </Empty>
  )
}

/** The customer the server matched, loaded in full so the picker can show it. */
async function loadCustomer(id: number): Promise<PickedCustomer> {
  const customer = await unwrap(api.GET('/api/customers/{id}', { params: { path: { id } } }))
  return {
    id: customer.id,
    code: customer.code,
    name: customer.name,
    gstin: customer.gstin,
    city: customer.billingAddress.city,
    stateName: customer.billingAddress.stateName,
  }
}

export function SmartOrderPage() {
  const status = useAiStatus()
  const extract = useExtractOrder()
  const [tab, setTab] = useState<'text' | 'file'>('text')
  const [text, setText] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const [customer, setCustomer] = useState<PickedCustomer | null>(null)
  const [result, setResult] = useState<{ extraction: OrderExtraction; customer: PickedCustomer | null } | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const hasContent = tab === 'text' ? text.trim().length > 0 : file !== null
  const canRead = hasContent && customer !== null

  async function chooseFile(chosen: File | undefined) {
    setFileError(null)
    if (!chosen) return
    if (!ACCEPTED.split(',').includes(chosen.type)) {
      setFileError(t('Choose a PDF or an image (JPEG, PNG, WebP or GIF).'))
      return
    }
    try {
      const prepared = await prepareUpload(chosen)
      if (prepared.size > (prepared.type === 'application/pdf' ? 10 : 5) * 1024 * 1024) {
        setFileError(prepared.type === 'application/pdf' ? t('PDFs can be at most 10 MB.') : t('Images can be at most 5 MB.'))
        return
      }
      setFile(prepared)
    } catch {
      setFileError(t("That image couldn't be opened. Try a JPEG or PNG."))
    }
  }

  async function read() {
    let extraction: OrderExtraction
    try {
      extraction = await extract.mutateAsync({
        text: tab === 'text' ? text : undefined,
        file: tab === 'file' ? (file ?? undefined) : undefined,
        customerId: customer?.id,
      })
    } catch {
      return // shown from extract.error
    }
    const matched = extraction.customer.customerId
    const picked = matched ? (matched === customer?.id ? customer : await loadCustomer(matched).catch(() => null)) : null
    setResult({ extraction, customer: picked })
  }

  if (status.isPending) return <Skeleton className="h-96 w-full rounded-xl" />

  return (
    <>
      <PageHeader
        title={t('Smart order entry')}
        description={t('Paste a WhatsApp message or email, or upload a PDF or photo of the order. Claude reads it into a draft you check before anything is saved.')}
        actions={
          <Button asChild variant="ghost">
            <Link to="/orders">
              <ArrowLeft /> {t('Orders')}
            </Link>
          </Button>
        }
      />

      {!status.data?.orderEntry ? (
        <AiOff reason={status.data?.reason} />
      ) : result ? (
        <ReviewOrder extraction={result.extraction} initialCustomer={result.customer} onStartOver={() => setResult(null)} />
      ) : (
        <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,48rem)_minmax(16rem,1fr)]">
          <Card>
            <CardHeader>
              <CardTitle>{t("The customer's order")}</CardTitle>
              <CardDescription>{t('English, Tamil (தமிழ்) or a mix of both, with trade shorthand like "30s cc", "10 bags", "2k mtrs".')}</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-5">
              <div className="relative">
                {extract.isPending && <ScanOverlay />}
                <Tabs value={tab} onValueChange={(next) => setTab(next as 'text' | 'file')}>
                  <TabsList>
                    <TabsTrigger value="text">
                      <FileText /> {t('Paste text')}
                    </TabsTrigger>
                    <TabsTrigger value="file">
                      <Paperclip /> {t('Upload PDF or photo')}
                    </TabsTrigger>
                  </TabsList>
                  <TabsContent value="text" className="pt-2">
                    <Label htmlFor="order-text" className="sr-only">
                      {t('Order text')}
                    </Label>
                    <Textarea
                      id="order-text"
                      rows={10}
                      value={text}
                      onChange={(e) => setText(e.target.value)}
                      placeholder={EXAMPLE}
                      maxLength={20000}
                      disabled={extract.isPending}
                    />
                  </TabsContent>
                  <TabsContent value="file" className="pt-2">
                    <input
                      ref={fileInput}
                      type="file"
                      accept={ACCEPTED}
                      className="sr-only"
                      aria-label={t('Order file')}
                      onChange={(e) => {
                        void chooseFile(e.target.files?.[0])
                        e.target.value = ''
                      }}
                    />
                    {file ? (
                      <div className="flex items-center gap-3 rounded-lg border p-3">
                        {file.type === 'application/pdf' ? <FileText className="size-5 text-muted-foreground" /> : <ImageIcon className="size-5 text-muted-foreground" />}
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm font-medium">{file.name}</p>
                          <p className="text-xs text-muted-foreground">{formatSize(file.size)}</p>
                        </div>
                        <Button variant="ghost" size="icon" onClick={() => setFile(null)} aria-label={t('Remove file')} disabled={extract.isPending}>
                          <X />
                        </Button>
                      </div>
                    ) : (
                      <button
                        type="button"
                        onClick={() => fileInput.current?.click()}
                        onDragOver={(e) => e.preventDefault()}
                        onDrop={(e) => {
                          e.preventDefault()
                          void chooseFile(e.dataTransfer.files[0])
                        }}
                        className="flex w-full flex-col items-center gap-2 rounded-lg border border-dashed p-8 text-sm text-muted-foreground transition-colors hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                      >
                        <Paperclip className="size-5" />
                        <span>{t('Drop a PDF or photo here, or click to choose one')}</span>
                        <span className="text-xs">{t('Photos are resized before upload. PDFs up to 10 MB.')}</span>
                      </button>
                    )}
                    {fileError && <p className="mt-2 text-sm text-destructive">{fileError}</p>}
                  </TabsContent>
                </Tabs>
              </div>

              <div className="flex flex-col gap-2">
                <Label htmlFor="known-customer">{t('Customer')}</Label>
                <div className="max-w-md">
                  <CustomerPicker id="known-customer" value={customer} onChange={setCustomer} disabled={extract.isPending} />
                </div>
                <p className="text-xs text-muted-foreground">
                  {t("Who sent this order? If the order's GSTIN, phone or name points to someone else, you'll be warned before saving.")}
                </p>
              </div>

              {extract.error && (
                <Alert variant="destructive">
                  <AlertDescription>{errorMessage(extract.error)}</AlertDescription>
                </Alert>
              )}
            </CardContent>
            <CardFooter className="flex flex-wrap items-center gap-3">
              <Button onClick={() => void read()} disabled={!canRead || extract.isPending}>
                {extract.isPending ? <Spinner /> : <Sparkles />} {t('Read order')}
              </Button>
              <p className="text-xs text-muted-foreground">
                {extract.isPending
                  ? t('Reading the order. This usually takes 10 to 30 seconds.')
                  : hasContent && !customer
                    ? t('Choose the customer first.')
                    : t("The order is sent to Anthropic's Claude API to be read. Nothing is saved until you confirm.")}
              </p>
            </CardFooter>
          </Card>

          <Card className="hidden overflow-hidden lg:sticky lg:top-20 lg:flex">
            <CardContent className="flex flex-col items-center gap-4 text-center">
              <AiOrb3D active={extract.isPending} className="size-44" fallback={<StillOrb active={extract.isPending} className="size-44" />} />
              <div className="space-y-1">
                <p className="font-medium">{extract.isPending ? t('Reading the order…') : t('Ready when you are')}</p>
                <p className="text-sm text-muted-foreground">
                  {extract.isPending
                    ? t('Claude is matching each line to your catalogue and checking who sent it.')
                    : t('Claude reads the order, matches each line to your products and drafts it for you to check.')}
                </p>
              </div>
              <ul className="w-full space-y-2 text-left text-sm">
                {[
                  t('Reads the message, PDF or photo'),
                  t('Matches lines to your products'),
                  t("Checks the customer's details"),
                  t('Drafts the order for your review'),
                ].map((step) => (
                  <li key={step} className="flex items-center gap-2 text-muted-foreground">
                    <Sparkles className="size-3.5 shrink-0 text-brand" />
                    {step}
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        </div>
      )}
    </>
  )
}
