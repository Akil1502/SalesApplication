import { useUserSetting } from '@/lib/user-settings'

export type Density = 'comfortable' | 'compact'

export interface TableSettings {
  /** Keys of the columns this person has hidden. */
  hidden: string[]
  density: Density
}

const DEFAULTS: TableSettings = { hidden: [], density: 'comfortable' }

/** How a person likes a list laid out (columns shown, row spacing), remembered on the server per list. */
export function useTableSettings(listKey: string) {
  const [settings, setSettings] = useUserSetting<TableSettings>(`table.${listKey}`, DEFAULTS)
  return {
    hidden: settings.hidden ?? [],
    density: settings.density ?? 'comfortable',
    toggleColumn: (key: string) =>
      setSettings((current) => ({
        ...current,
        hidden: current.hidden?.includes(key) ? current.hidden.filter((k) => k !== key) : [...(current.hidden ?? []), key],
      })),
    setDensity: (density: Density) => setSettings((current) => ({ ...current, density })),
    reset: () => setSettings(DEFAULTS),
  }
}

export interface SavedView {
  id: string
  name: string
  /** The list's address query: filters, search and sort (never the page number). */
  search: string
}

export const NO_VIEWS: SavedView[] = []

/** A list's saved views, remembered on the server per person. */
export function useSavedViews(listKey: string) {
  return useUserSetting<SavedView[]>(`views.${listKey}`, NO_VIEWS)
}

/** The parts of a list's address that make up a view: everything but the page number, in a stable order. */
export function viewSearch(search: string): string {
  const params = new URLSearchParams(search)
  params.delete('page')
  params.sort()
  const text = params.toString()
  return text ? `?${text}` : ''
}
