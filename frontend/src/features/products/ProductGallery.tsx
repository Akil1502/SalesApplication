import { TriangleAlert } from 'lucide-react'
import { EmptyArt } from '@/components/fx/EmptyArt'
import { TiltCard } from '@/components/fx/TiltCard'
import { Card } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { formatINR, formatQuantity } from '@/lib/format'
import { initials, placeholderStyle } from '@/lib/product-tile'
import type { ProductListItem } from './api'
import { useProductImages } from './images'
import { t } from '@/lib/i18n'

interface ProductGalleryProps {
  items: ProductListItem[] | undefined
  isLoading: boolean
  onOpen: (product: ProductListItem) => void
  emptyTitle: string
  emptyDescription?: string
}

/** Products as photo cards that tilt towards the pointer: the catalogue as a customer would see it. */
export function ProductGallery({ items, isLoading, onOpen, emptyTitle, emptyDescription }: ProductGalleryProps) {
  const images = useProductImages().data

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {Array.from({ length: 8 }, (_, i) => (
          <Skeleton key={i} className="aspect-[4/4.2] w-full rounded-xl" />
        ))}
      </div>
    )
  }

  if (!items?.length) {
    return (
      <Empty className="rounded-xl border py-12">
        <EmptyHeader>
          <EmptyMedia>
            <EmptyArt />
          </EmptyMedia>
          <EmptyTitle>{emptyTitle}</EmptyTitle>
          {emptyDescription && <EmptyDescription>{emptyDescription}</EmptyDescription>}
        </EmptyHeader>
      </Empty>
    )
  }

  return (
    <div className="fx-stagger grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {items.map((product) => {
        const image = images?.get(product.id)
        const low = product.quantityOnHand <= product.reorderLevel
        return (
          <TiltCard key={product.id} max={8} className="rounded-xl">
            <button
              type="button"
              onClick={() => onOpen(product)}
              className="block w-full rounded-xl text-left focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
            >
              <Card className="gap-0 overflow-hidden py-0">
                <div className="relative aspect-[4/3] overflow-hidden" style={image ? undefined : placeholderStyle(product.id)}>
                  {image ? (
                    <img
                      src={image.thumbnailUrl}
                      srcSet={`${image.thumbnailUrl} 400w, ${image.imageUrl} 1200w`}
                      sizes="(min-width: 1280px) 25vw, (min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw"
                      alt={t('Photo of {name}', { name: product.name })}
                      loading="lazy"
                      decoding="async"
                      className="size-full object-cover transition-transform duration-700 ease-out group-hover/tilt:scale-105"
                    />
                  ) : (
                    <span aria-hidden className="absolute inset-0 flex items-center justify-center text-4xl font-semibold text-[oklch(0.3_0.05_268/0.6)]">
                      {initials(product.name)}
                    </span>
                  )}
                  <span className="fx-glass absolute top-2 left-2 rounded-full px-2 py-0.5 text-xs font-medium">{product.categoryName}</span>
                  {low && (
                    <span className="absolute top-2 right-2 inline-flex items-center gap-1 rounded-full bg-amber-500/90 px-2 py-0.5 text-xs font-medium text-white">
                      <TriangleAlert className="size-3" /> {t('Low stock')}
                    </span>
                  )}
                  {!product.isActive && <span className="absolute right-2 bottom-2 rounded-full bg-black/60 px-2 py-0.5 text-xs text-white">{t('Inactive')}</span>}
                </div>
                <div className="space-y-1 p-4">
                  <p className="truncate font-medium" title={product.name}>
                    {product.name}
                  </p>
                  <p className="font-mono text-xs text-muted-foreground">{product.sku}</p>
                  <div className="flex flex-wrap items-baseline justify-between gap-x-2 pt-1">
                    <span className="font-semibold tabular-nums">
                      {formatINR(product.price)} <span className="text-xs font-normal text-muted-foreground">/ {product.unit}</span>
                    </span>
                    <span className="truncate text-xs text-muted-foreground">{t('{quantity} in stock', { quantity: formatQuantity(product.quantityOnHand, product.unit) })}</span>
                  </div>
                </div>
              </Card>
            </button>
          </TiltCard>
        )
      })}
    </div>
  )
}
