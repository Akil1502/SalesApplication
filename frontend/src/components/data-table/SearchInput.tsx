import { Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Input } from '@/components/ui/input'

interface SearchInputProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  delayMs?: number
}

/** Search box that waits for a pause in typing before updating the list, so we don't query on every keystroke. */
export function SearchInput({ value, onChange, placeholder = 'Search…', delayMs = 300 }: SearchInputProps) {
  const [draft, setDraft] = useState(value)

  // Follow external changes (Back button, cleared filters) by adjusting state during render,
  // React's recommended alternative to syncing props into state with an effect.
  const [lastValue, setLastValue] = useState(value)
  if (value !== lastValue) {
    setLastValue(value)
    setDraft(value)
  }

  useEffect(() => {
    if (draft === value) return
    const timer = setTimeout(() => onChange(draft.trim()), delayMs)
    return () => clearTimeout(timer)
  }, [draft, value, onChange, delayMs])

  return (
    <div className="relative w-full max-w-sm">
      <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input
        type="search"
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        placeholder={placeholder}
        className="pl-8"
        aria-label={placeholder}
        // "/" anywhere on the page jumps here.
        data-page-search
        aria-keyshortcuts="/"
      />
    </div>
  )
}
