import { useState } from 'react'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useProductImage } from '@/features/products/images'
import { initials, placeholderStyle } from '@/lib/product-tile'
import { cn } from '@/lib/utils'

const sizes = {
  xs: 'size-6 rounded text-[9px]',
  sm: 'size-9 rounded-md text-[11px]',
  md: 'size-12 rounded-lg text-sm',
  lg: 'size-20 rounded-xl text-lg',
} as const

interface ProductThumbProps {
  productId: number
  name?: string
  size?: keyof typeof sizes
  /** Show the larger photo on hover. */
  preview?: boolean
  className?: string
}

/** A product's photo as a small square, or a coloured tile with its initials until it has one. */
export function ProductThumb({ productId, name, size = 'sm', preview = false, className }: ProductThumbProps) {
  const image = useProductImage(productId)
  const [loadedUrl, setLoadedUrl] = useState<string>()

  const tile = (
    <span
      className={cn('relative inline-flex shrink-0 items-center justify-center overflow-hidden ring-1 ring-foreground/10', sizes[size], className)}
      style={image ? undefined : placeholderStyle(productId)}
      aria-hidden={!image}
    >
      {image ? (
        <img
          key={image.thumbnailUrl}
          src={image.thumbnailUrl}
          alt={name ? `Photo of ${name}` : 'Product photo'}
          loading="lazy"
          decoding="async"
          onLoad={() => setLoadedUrl(image.thumbnailUrl)}
          className={cn(
            'size-full object-cover transition-[opacity,transform] duration-500 ease-out',
            loadedUrl === image.thumbnailUrl ? 'scale-100 opacity-100' : 'scale-110 opacity-0',
          )}
        />
      ) : (
        <span className="font-semibold tracking-tight text-[oklch(0.3_0.05_268)]">{initials(name)}</span>
      )}
    </span>
  )

  if (!preview || !image) return tile
  return (
    <Tooltip>
      <TooltipTrigger asChild>{tile}</TooltipTrigger>
      <TooltipContent side="right" className="p-1">
        <img src={image.imageUrl} alt="" className="block max-h-60 max-w-60 rounded-md object-contain" />
      </TooltipContent>
    </Tooltip>
  )
}
