import { useEffect } from 'react'
import { brandName, useBranding } from './api'
import { applyBranding } from './apply'

/** Keeps the page in the company's colours and the browser tab showing its logo. Renders nothing. */
export function BrandingSync() {
  const branding = useBranding().data
  useEffect(() => {
    if (branding) applyBranding(branding, brandName(branding))
  }, [branding])
  return null
}
