import { useQuery } from '@tanstack/react-query'
import { Check, ChevronsUpDown } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { cn } from '@/lib/utils'

export type PickedSupplier = Pick<Schemas['SupplierListItemDto'], 'id' | 'code' | 'name' | 'gstin' | 'city' | 'stateName'>

interface SupplierPickerProps {
  value: PickedSupplier | null
  onChange: (supplier: PickedSupplier | null) => void
  disabled?: boolean
  invalid?: boolean
  id?: string
  /** Offers "No supplier" at the top of the list, to clear the choice. */
  allowNone?: boolean
}

/** Searchable supplier dropdown (name, code, GSTIN, phone or city), searched on the server. */
export function SupplierPicker({ value, onChange, disabled, invalid, id, allowNone }: SupplierPickerProps) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  useEffect(() => {
    const timer = setTimeout(() => setQ(search.trim()), 250)
    return () => clearTimeout(timer)
  }, [search])

  const results = useQuery({
    queryKey: ['suppliers', 'picker', q],
    queryFn: () => unwrap(api.GET('/api/suppliers', { params: { query: { q: q || undefined, pageSize: 20, sort: 'name' } } })),
    enabled: open,
    staleTime: 30_000,
  })

  const pick = (supplier: PickedSupplier | null) => {
    onChange(supplier)
    setOpen(false)
    setSearch('')
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-invalid={invalid}
          disabled={disabled}
          className={cn('w-full justify-between font-normal', !value && 'text-muted-foreground')}
        >
          <span className="truncate">{value ? (value.code ? `${value.name} (${value.code})` : value.name) : allowNone ? 'No supplier' : 'Choose a supplier…'}</span>
          <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-(--radix-popover-trigger-width) min-w-80 p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput placeholder="Search name, code, GSTIN, city…" value={search} onValueChange={setSearch} />
          <CommandList>
            <CommandEmpty>{results.isFetching ? 'Searching…' : 'No active suppliers match.'}</CommandEmpty>
            <CommandGroup>
              {allowNone && value && (
                <CommandItem value="none" onSelect={() => pick(null)}>
                  <Check className="opacity-0" />
                  <span className="text-muted-foreground">No supplier</span>
                </CommandItem>
              )}
              {results.data?.items.map((supplier) => (
                <CommandItem key={supplier.id} value={String(supplier.id)} onSelect={() => pick(supplier)}>
                  <Check className={cn(value?.id === supplier.id ? 'opacity-100' : 'opacity-0')} />
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate">{supplier.name}</span>
                    <span className="text-xs text-muted-foreground">
                      {supplier.code} · {supplier.city}, {supplier.stateName}
                      {supplier.gstin ? ` · ${supplier.gstin}` : ' · unregistered'}
                    </span>
                  </div>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
