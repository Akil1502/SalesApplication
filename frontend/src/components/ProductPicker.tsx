import { useQuery } from '@tanstack/react-query'
import { Check, ChevronsUpDown } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Spinner } from '@/components/ui/spinner'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { formatQuantity } from '@/lib/format'
import { cn } from '@/lib/utils'

export type PickedProduct = Pick<Schemas['ProductListItemDto'], 'id' | 'sku' | 'name' | 'unit' | 'quantityOnHand' | 'listPrice' | 'gstRate'>

interface ProductPickerProps {
  value: PickedProduct | null
  onChange: (product: PickedProduct) => void
  /** Product ids that can't be picked again (already on the document). */
  excludeIds?: number[]
  disabled?: boolean
  invalid?: boolean
  id?: string
}

function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}

/**
 * Searchable product dropdown. Searches the server (SKU, name or HSN) as you type, so it works the same
 * with 20 products or 20,000. Used on receipts now and on order lines in P3.
 */
export function ProductPicker({ value, onChange, excludeIds = [], disabled, invalid, id }: ProductPickerProps) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim(), 250)

  const results = useQuery({
    queryKey: ['products', 'picker', q],
    queryFn: () => unwrap(api.GET('/api/products', { params: { query: { q: q || undefined, pageSize: 20, sort: 'name' } } })),
    enabled: open,
    staleTime: 30_000,
  })

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
          <span className="truncate">{value ? `${value.sku} · ${value.name}` : 'Choose a product…'}</span>
          <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-(--radix-popover-trigger-width) min-w-80 p-0" align="start">
        {/* Filtering happens on the server, so cmdk's own filter is off. */}
        <Command shouldFilter={false}>
          <CommandInput placeholder="Search SKU, name or HSN…" value={search} onValueChange={setSearch} />
          <CommandList>
            {results.isFetching && !results.data && (
              <div className="flex justify-center py-6">
                <Spinner />
              </div>
            )}
            <CommandEmpty>{results.isFetching ? 'Searching…' : 'No active products match.'}</CommandEmpty>
            <CommandGroup>
              {results.data?.items.map((product) => {
                const excluded = excludeIds.includes(product.id) && product.id !== value?.id
                return (
                  <CommandItem
                    key={product.id}
                    value={String(product.id)}
                    disabled={excluded}
                    onSelect={() => {
                      onChange(product)
                      setOpen(false)
                      setSearch('')
                    }}
                  >
                    <Check className={cn(value?.id === product.id ? 'opacity-100' : 'opacity-0')} />
                    <div className="flex min-w-0 flex-1 flex-col">
                      <span className="truncate">{product.name}</span>
                      <span className="text-xs text-muted-foreground">
                        {product.sku} · {formatQuantity(product.quantityOnHand, product.unit)} in stock
                        {excluded && ' · already added'}
                      </span>
                    </div>
                  </CommandItem>
                )
              })}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
