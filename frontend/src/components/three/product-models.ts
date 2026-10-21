// Kept apart from the 3D code, so pages can choose a model without downloading the 3D library.

/** Which 3D model stands in for a product without a photo. */
export type ProductModel = 'yarn' | 'fabric' | 'stack' | 'carton'

/** The model for a product, from its category's name. */
export function modelForCategory(category: string | undefined): ProductModel {
  const name = (category ?? '').toLowerCase()
  if (name.includes('yarn') || name.includes('thread')) return 'yarn'
  if (name.includes('fabric') || name.includes('cloth')) return 'fabric'
  if (name.includes('pack') || name.includes('box') || name.includes('carton')) return 'carton'
  return 'stack'
}
