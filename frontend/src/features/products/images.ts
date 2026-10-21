import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { fitWithin } from '@/features/sales/smart-entry/image'
import { t } from '@/lib/i18n'

export type ProductImage = Schemas['ProductImageDto']

/** The large picture's long edge, and the square thumbnail's side, in pixels. */
export const PHOTO_EDGE = 1200
export const THUMBNAIL_EDGE = 400

const imagesKey = ['products', 'images'] as const

/**
 * Every product photo, by product id. One small request serves every screen that shows a picture next to a
 * product; picture addresses change with each new photo, so the browser caches the pictures themselves.
 */
export function useProductImages() {
  return useQuery({
    queryKey: imagesKey,
    queryFn: () => unwrap(api.GET('/api/products/images')),
    staleTime: 5 * 60_000,
    select: (images) => new Map(images.map((image) => [image.productId, image])),
  })
}

export function useProductImage(productId: number | undefined): ProductImage | undefined {
  return useProductImages().data?.get(productId ?? 0)
}

async function toJpeg(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality))
  if (!blob) throw new Error(t("This picture couldn't be read. Try a JPEG or PNG file."))
  return blob
}

function canvasOf(width: number, height: number): [HTMLCanvasElement, CanvasRenderingContext2D] {
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')
  if (!context) throw new Error(t("This browser can't prepare pictures."))
  // Transparent PNGs get a white background rather than turning black as JPEGs.
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, width, height)
  return [canvas, context]
}

/** Where to crop a centred square from a picture. */
export function centreSquare(width: number, height: number): { x: number; y: number; side: number } {
  const side = Math.min(width, height)
  return { x: Math.round((width - side) / 2), y: Math.round((height - side) / 2), side }
}

/**
 * Prepares a photo for upload in the browser: the picture shrunk to at most 1200px on the long edge, and a
 * centred 400px square thumbnail for lists and pickers. Both are JPEGs, small enough to upload in a moment.
 */
export async function preparePhoto(file: File): Promise<{ image: Blob; thumbnail: Blob }> {
  if (!/^image\/(jpeg|png|webp)$/.test(file.type)) throw new Error(t('Choose a JPEG, PNG or WebP picture.'))
  const bitmap = await createImageBitmap(file)
  try {
    const size = fitWithin(bitmap.width, bitmap.height, PHOTO_EDGE)
    const [large, largeContext] = canvasOf(size.width, size.height)
    largeContext.drawImage(bitmap, 0, 0, size.width, size.height)

    const square = centreSquare(bitmap.width, bitmap.height)
    const side = Math.min(THUMBNAIL_EDGE, square.side)
    const [small, smallContext] = canvasOf(side, side)
    smallContext.drawImage(bitmap, square.x, square.y, square.side, square.side, 0, 0, side, side)

    return { image: await toJpeg(large, 0.86), thumbnail: await toJpeg(small, 0.82) }
  } finally {
    bitmap.close()
  }
}

function useRefreshImages() {
  const queryClient = useQueryClient()
  return (productId: number) => {
    void queryClient.invalidateQueries({ queryKey: imagesKey })
    void queryClient.invalidateQueries({ queryKey: ['products', 'detail', productId] })
  }
}

export function useSetProductImage() {
  const refresh = useRefreshImages()
  return useMutation({
    mutationFn: async ({ productId, file }: { productId: number; file: File }) => {
      const { image, thumbnail } = await preparePhoto(file)
      const form = new FormData()
      form.append('image', image, 'photo.jpg')
      form.append('thumbnail', thumbnail, 'thumbnail.jpg')
      // The browser sets the multipart boundary itself when the body is FormData.
      return unwrap(api.PUT('/api/products/{id}/image', { params: { path: { id: productId } }, body: {} as never, bodySerializer: () => form }))
    },
    onSuccess: (_, { productId }) => refresh(productId),
  })
}

export function useRemoveProductImage() {
  const refresh = useRefreshImages()
  return useMutation({
    mutationFn: (productId: number) => unwrap(api.DELETE('/api/products/{id}/image', { params: { path: { id: productId } } })),
    onSuccess: (_, productId) => refresh(productId),
  })
}
