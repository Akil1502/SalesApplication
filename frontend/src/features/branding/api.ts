import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { fitWithin } from '@/features/sales/smart-entry/image'
import { cachedBranding } from './apply'

export type Branding = Schemas['BrandingDto']

export const brandingKey = ['branding'] as const

/** The logo's long edge in pixels: sharp in the menu and on a printed page, small enough to load at once. */
export const LOGO_EDGE = 512

const MAX_LOGO_BYTES = 480 * 1024

/**
 * The company's name, colour and logo. Open before sign-in (the sign-in page shows them). Starts from the copy the
 * browser kept last time, so the first screen is already in the company's colours.
 */
export function useBranding() {
  return useQuery({
    queryKey: brandingKey,
    queryFn: () => unwrap(api.GET('/api/branding')),
    staleTime: 5 * 60_000,
    initialData: cachedBranding,
    initialDataUpdatedAt: 0,
  })
}

/** What the app calls itself: the company's name once it has a logo, otherwise SalesApp. */
export function brandName(branding: Branding | undefined): string {
  return branding?.logoUrl ? branding.companyName : 'SalesApp'
}

export function useBrandName(): string {
  return brandName(useBranding().data)
}

/** Sets the browser tab's title: the page, then the company (or SalesApp). */
export function useDocumentTitle(page: string | undefined) {
  const name = useBrandName()
  useEffect(() => {
    document.title = page ? `${page} · ${name}` : name
  }, [page, name])
}

async function encode(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<Blob | null> {
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, quality))
  // Browsers that can't write a format quietly write PNG instead.
  return blob && blob.type === type ? blob : null
}

/**
 * Prepares a logo in the browser: at most 512px on the long edge, as PNG so a transparent background stays
 * transparent; WebP (also transparent) or JPEG only if a detailed logo would be too large as PNG.
 */
export async function prepareLogo(file: File): Promise<Blob> {
  if (!/^image\/(jpeg|png|webp)$/.test(file.type)) throw new Error('Choose a PNG, JPEG or WebP picture.')
  const bitmap = await createImageBitmap(file)
  try {
    const size = fitWithin(bitmap.width, bitmap.height, LOGO_EDGE)
    const canvas = document.createElement('canvas')
    canvas.width = size.width
    canvas.height = size.height
    const context = canvas.getContext('2d')
    if (!context) throw new Error("This browser can't prepare pictures.")
    context.drawImage(bitmap, 0, 0, size.width, size.height)
    for (const [type, quality] of [['image/png'], ['image/webp', 0.92], ['image/jpeg', 0.9]] as const) {
      if (type === 'image/jpeg') {
        // No transparency in JPEG: put the logo on white rather than black.
        context.globalCompositeOperation = 'destination-over'
        context.fillStyle = '#ffffff'
        context.fillRect(0, 0, size.width, size.height)
      }
      const blob = await encode(canvas, type, quality)
      if (blob && blob.size <= MAX_LOGO_BYTES) return blob
    }
    throw new Error('This picture is too detailed for a logo. Try a simpler or smaller file.')
  } finally {
    bitmap.close()
  }
}

function useRefreshBranding() {
  const queryClient = useQueryClient()
  // The logo has its own table, so the settings form (and its unsaved edits) is left alone.
  return (branding: Branding) => queryClient.setQueryData(brandingKey, branding)
}

export function useSetLogo() {
  const refresh = useRefreshBranding()
  return useMutation({
    mutationFn: async (file: File) => {
      const logo = await prepareLogo(file)
      const form = new FormData()
      form.append('logo', logo, `logo.${logo.type.split('/')[1]}`)
      // The browser sets the multipart boundary itself when the body is FormData.
      return unwrap(api.PUT('/api/settings/company/logo', { body: {} as never, bodySerializer: () => form }))
    },
    onSuccess: refresh,
  })
}

export function useRemoveLogo() {
  const refresh = useRefreshBranding()
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/settings/company/logo')),
    onSuccess: refresh,
  })
}
