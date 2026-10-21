import { useUserSetting } from '@/lib/user-settings'

/** A record someone starred or opened: where it lives, what it's called, and which list it's from. */
export interface RememberedRecord {
  path: string
  label: string
  list: string
  at?: string
}

const NONE: RememberedRecord[] = []
const MAX_FAVOURITES = 50
const MAX_RECENT = 12

/** Starred records, newest first, kept per person on the server. */
export function useFavourites() {
  const [favourites, setFavourites] = useUserSetting<RememberedRecord[]>('favourites', NONE)
  return {
    favourites,
    isFavourite: (path: string) => favourites.some((f) => f.path === path),
    toggle: (record: RememberedRecord) =>
      setFavourites((list) =>
        list.some((f) => f.path === record.path) ? list.filter((f) => f.path !== record.path) : [record, ...list].slice(0, MAX_FAVOURITES),
      ),
  }
}

/** The records opened lately, newest first. */
export function useRecent() {
  const [recent, setRecent] = useUserSetting<RememberedRecord[]>('recent', NONE)
  return {
    recent,
    remember: (record: RememberedRecord) =>
      setRecent((list) => [{ ...record, at: new Date().toISOString() }, ...list.filter((r) => r.path !== record.path)].slice(0, MAX_RECENT)),
  }
}
