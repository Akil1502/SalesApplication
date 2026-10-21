import { FileText, ImageIcon, Sparkles, Upload, X } from 'lucide-react'
import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import { prepareUpload } from '@/features/sales/smart-entry/image'
import { errorMessage } from '@/lib/api/errors'
import { useReadBill, type BillExtraction } from './api'
import { t } from '@/lib/i18n'

const ACCEPTED = 'application/pdf,image/jpeg,image/png,image/webp,image/gif'

/**
 * Upload the supplier's invoice and let AI fill in the bill below. Nothing is saved: the clerk checks the filled-in
 * form, with any warnings, and saves it as usual.
 */
export function ReadBillCard({ purchaseOrderId, onRead }: { purchaseOrderId: number; onRead: (extraction: BillExtraction) => void }) {
  const read = useReadBill()
  const input = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)

  async function choose(chosen: File | undefined) {
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

  async function run() {
    if (!file) return
    try {
      const extraction = await read.mutateAsync({ purchaseOrderId, file })
      onRead(extraction)
      toast.success(t('Bill read (cost ₹{cost}). Check the details below before saving.', { cost: extraction.usage.costInr.toFixed(2) }))
    } catch (error) {
      toast.error(errorMessage(error))
    }
  }

  return (
    <Card className="border-violet-600/30">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Sparkles className="size-4 text-violet-600" />
          {t('Read the bill with AI')}
        </CardTitle>
        <CardDescription>{t("Upload the supplier's invoice (PDF or photo) and the form below is filled in from it, matched to this order. Nothing is saved until you do.")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <input
          ref={input}
          type="file"
          accept={ACCEPTED}
          className="sr-only"
          aria-label={t("Supplier's bill")}
          onChange={(e) => {
            void choose(e.target.files?.[0])
            e.target.value = ''
          }}
        />
        <div className="flex flex-wrap items-center gap-3">
          {file ? (
            <div className="flex min-w-64 flex-1 items-center gap-3 rounded-lg border p-2">
              {file.type === 'application/pdf' ? <FileText className="size-5 text-muted-foreground" /> : <ImageIcon className="size-5 text-muted-foreground" />}
              <span className="min-w-0 flex-1 truncate text-sm">{file.name}</span>
              <Button type="button" variant="ghost" size="icon-sm" aria-label={t('Remove file')} disabled={read.isPending} onClick={() => setFile(null)}>
                <X />
              </Button>
            </div>
          ) : (
            <Button type="button" variant="outline" onClick={() => input.current?.click()}>
              <Upload />
              {t('Choose file')}
            </Button>
          )}
          <Button type="button" disabled={!file || read.isPending} onClick={run}>
            {read.isPending ? <Spinner /> : <Sparkles />}
            {read.isPending ? t('Reading… (10 to 30 seconds)') : t('Read bill')}
          </Button>
        </div>
        {fileError && <p className="text-sm text-destructive">{fileError}</p>}
      </CardContent>
    </Card>
  )
}
