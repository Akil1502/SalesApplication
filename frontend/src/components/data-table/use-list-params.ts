import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'

export interface ListParams {
  q: string
  page: number
  pageSize: number
  sort: string | undefined
  includeInactive: boolean
  /** Extra list-specific filters (categoryId, stateCode, ...), as raw strings. */
  filters: Record<string, string>
}

const RESERVED = new Set(['q', 'page', 'pageSize', 'sort', 'includeInactive'])

/**
 * List state (search, page, sort, filters) lives in the URL query string, so a filtered list can
 * be bookmarked or shared, and Back returns to the same page of results.
 */
export function useListParams(defaults: { pageSize?: number; sort?: string } = {}) {
  const [searchParams, setSearchParams] = useSearchParams()

  const params = useMemo<ListParams>(() => {
    const filters: Record<string, string> = {}
    searchParams.forEach((value, key) => {
      if (!RESERVED.has(key) && value) filters[key] = value
    })
    return {
      q: searchParams.get('q') ?? '',
      page: Math.max(1, Number(searchParams.get('page')) || 1),
      pageSize: Number(searchParams.get('pageSize')) || defaults.pageSize || 20,
      sort: searchParams.get('sort') ?? defaults.sort,
      includeInactive: searchParams.get('includeInactive') === 'true',
      filters,
    }
  }, [searchParams, defaults.pageSize, defaults.sort])

  /** Update some params. Anything other than a page change resets to page 1. */
  const update = useCallback(
    (changes: Partial<Omit<ListParams, 'filters'>> & { filters?: Record<string, string | undefined> }) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current)
          const set = (key: string, value: string | number | boolean | undefined) => {
            if (value === undefined || value === '' || value === false) next.delete(key)
            else next.set(key, String(value))
          }

          if ('q' in changes) set('q', changes.q)
          if ('sort' in changes) set('sort', changes.sort)
          if ('pageSize' in changes) set('pageSize', changes.pageSize)
          if ('includeInactive' in changes) set('includeInactive', changes.includeInactive)
          for (const [key, value] of Object.entries(changes.filters ?? {})) set(key, value)

          const onlyPageChanged = Object.keys(changes).every((key) => key === 'page')
          set('page', onlyPageChanged && changes.page && changes.page > 1 ? changes.page : undefined)
          return next
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  return [params, update] as const
}
