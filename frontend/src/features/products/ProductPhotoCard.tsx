import { ImagePlus, Trash2, Upload } from 'lucide-react'
import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { TiltCard } from '@/components/fx/TiltCard'
import { modelForCategory } from '@/components/three/product-models'
import { ProductStage3D } from '@/components/three/scenes'
import { Button } from '@/components/ui/button'
import { Card, CardDescription, CardTitle } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import { errorMessage } from '@/lib/api/errors'
import { placeholderStyle } from '@/lib/product-tile'
import { cn } from '@/lib/utils'
import type { Product } from './api'
import { useRemoveProductImage, useSetProductImage } from './images'
import { t } from '@/lib/i18n'

/** The still view: the photo on a tilting card, or the product's coloured tile until it has one. */
function StillPhoto({ product }: { product: Product }) {
  if (product.image) {
    return (
      <div className="flex size-full items-center justify-center p-6">
        <TiltCard max={10} className="rounded-xl">
          <img src={product.image.imageUrl} alt={t('Photo of {name}', { name: product.name })} className="block max-h-56 rounded-xl object-contain shadow-[var(--shadow-lifted)]" />
        </TiltCard>
      </div>
    )
  }
  return (
    <div className="flex size-full items-center justify-center">
      <span className="flex size-28 items-center justify-center rounded-2xl text-3xl font-semibold text-[oklch(0.3_0.05_268)] shadow-[var(--shadow-lifted)]" style={placeholderStyle(product.id)}>
        <ImagePlus className="size-10 opacity-60" />
      </span>
    </div>
  )
}

/**
 * The product's photo in 3D: on a glossy card that leans with the pointer, or a 3D model of its kind until it has
 * one. People who edit products can drop a picture on it or choose a file; it's shrunk in the browser first.
 */
export function ProductPhotoCard({ product, canEdit }: { product: Product; canEdit: boolean }) {
  const setImage = useSetProductImage()
  const removeImage = useRemoveProductImage()
  const input = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)
  const busy = setImage.isPending || removeImage.isPending

  const upload = (file: File | undefined) => {
    if (!file) return
    setImage.mutate(
      { productId: product.id, file },
      {
        onSuccess: () => toast.success(product.image ? t('Photo replaced') : t('Photo added')),
        onError: (error) => toast.error(errorMessage(error)),
      },
    )
  }

  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="grid sm:grid-cols-[1.25fr_1fr]">
        <div
          className={cn(
            'relative h-64 transition-shadow sm:h-72',
            dragging && 'shadow-[inset_0_0_0_3px_var(--brand)]',
          )}
          style={{
            background:
              'radial-gradient(circle at 50% 40%, color-mix(in oklch, var(--brand) 14%, transparent), transparent 65%), linear-gradient(180deg, transparent, color-mix(in oklch, var(--brand) 6%, transparent))',
          }}
          onDragOver={(event) => {
            if (!canEdit) return
            event.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={(event) => {
            if (!canEdit) return
            event.preventDefault()
            setDragging(false)
            upload(event.dataTransfer.files[0])
          }}
        >
          <ProductStage3D
            key={product.image?.imageUrl ?? 'model'}
            className="absolute inset-0"
            imageUrl={product.image?.imageUrl}
            model={modelForCategory(product.categoryName)}
            fallback={<StillPhoto product={product} />}
          />
          {busy && (
            <div className="absolute inset-0 flex items-center justify-center gap-2 bg-background/60 text-sm backdrop-blur-sm" role="status">
              <Spinner /> {setImage.isPending ? t('Preparing and uploading…') : t('Removing…')}
            </div>
          )}
          {dragging && (
            <div className="pointer-events-none absolute inset-x-0 bottom-3 text-center text-sm font-medium text-brand">{t('Drop the picture to use it')}</div>
          )}
        </div>

        <div className="flex flex-col justify-center gap-3 p-6">
          <CardTitle>{t('Photo')}</CardTitle>
          <CardDescription>
            {product.image
              ? t('Shown next to this product in lists, order entry, invoices and stock. Point at it to tilt it.')
              : t('No photo yet. Until there is one, a 3D model of its kind stands in, and lists show a coloured tile.')}
          </CardDescription>
          {canEdit && (
            <>
              <input
                ref={input}
                type="file"
                accept="image/jpeg,image/png,image/webp"
                className="hidden"
                onChange={(event) => {
                  upload(event.target.files?.[0])
                  event.target.value = ''
                }}
              />
              <div className="flex flex-wrap gap-2">
                <Button type="button" onClick={() => input.current?.click()} disabled={busy}>
                  <Upload />
                  {product.image ? t('Replace photo') : t('Add photo')}
                </Button>
                {product.image && (
                  <Button
                    type="button"
                    variant="ghost"
                    disabled={busy}
                    onClick={() =>
                      removeImage.mutate(product.id, {
                        onSuccess: () => toast.success(t('Photo removed')),
                        onError: (error) => toast.error(errorMessage(error)),
                      })
                    }
                  >
                    <Trash2 />
                    {t('Remove')}
                  </Button>
                )}
              </div>
              <p className="text-xs text-muted-foreground">{t("JPEG, PNG or WebP. Drop a file on the picture or choose one; it's resized in your browser before uploading.")}</p>
            </>
          )}
        </div>
      </div>
    </Card>
  )
}
