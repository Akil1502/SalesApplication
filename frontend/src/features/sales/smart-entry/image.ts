/** Claude reads images up to about this size on the long edge; anything larger only costs upload time and tokens. */
export const MAX_IMAGE_EDGE = 1568

/** Dimensions scaled to fit within `max` on the long edge, keeping the aspect ratio. Never scales up. */
export function fitWithin(width: number, height: number, max = MAX_IMAGE_EDGE): { width: number; height: number } {
  const scale = Math.min(1, max / Math.max(width, height))
  return { width: Math.round(width * scale), height: Math.round(height * scale) }
}

/**
 * Shrinks a photo in the browser before upload: phone photos are often 4000px and several MB, which is slow to send
 * and wasteful to process. PDFs and small images are returned unchanged.
 */
export async function prepareUpload(file: File): Promise<File> {
  if (!file.type.startsWith('image/') || file.type === 'image/gif') return file

  const bitmap = await createImageBitmap(file)
  try {
    const size = fitWithin(bitmap.width, bitmap.height)
    if (size.width === bitmap.width && file.size <= 1_500_000) return file

    const canvas = document.createElement('canvas')
    canvas.width = size.width
    canvas.height = size.height
    canvas.getContext('2d')?.drawImage(bitmap, 0, 0, size.width, size.height)
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.85))
    return blob ? new File([blob], file.name.replace(/\.\w+$/, '') + '.jpg', { type: 'image/jpeg' }) : file
  } finally {
    bitmap.close()
  }
}
