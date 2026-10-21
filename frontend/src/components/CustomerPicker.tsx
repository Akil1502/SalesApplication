import { useQuery } from '@tanstack/react-query'
import { Check, ChevronsUpDown } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { t } from '@/lib/i18n'
import { cn } from '@/lib/utils'

export type PickedCustomer = Pick<Schemas['CustomerListItemDto'], 'id' | 'code' | 'name' | 'gstin' | 'city' | 'stateName'>

interface CustomerPickerProps {
  value: PickedCustomer | null
  onChange: (customer: PickedCustomer) => void
  disabled?: boolean
  invalid?: boolean
  id?: string
}

/** Searchable customer dropdown (name, code, GSTIN, phone or city), searched on the server. */
export function CustomerPicker({ value, onChange, disabled, invalid, id }: CustomerPickerProps) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  useEffect(() => {
    const timer = setTimeout(() => setQ(search.trim()), 250)
    return () => clearTimeout(timer)
  }, [search])

  const results = useQuery({
    queryKey: ['customers', 'picker', q],
    queryFn: () => unwrap(api.GET('/api/customers', { params: { query: { q: q || undefined, pageSize: 20, sort: 'name' } } })),
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
          <span className="truncate">{value ? `${value.name} (${value.code})` : t('Choose a customer…')}</span>
          <ChevronsUpDown className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-(--radix-popover-trigger-width) min-w-80 p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput placeholder={t('Search name, code, GSTIN, city…')} value={search} onValueChange={setSearch} />
          <CommandList>
            <CommandEmpty>{results.isFetching ? t('Searching…') : t('No active customers match.')}</CommandEmpty>
            <CommandGroup>
              {results.data?.items.map((customer) => (
                <CommandItem
                  key={customer.id}
                  value={String(customer.id)}
                  onSelect={() => {
                    onChange(customer)
                    setOpen(false)
                    setSearch('')
                  }}
                >
                  <Check className={cn(value?.id === customer.id ? 'opacity-100' : 'opacity-0')} />
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate">{customer.name}</span>
                    <span className="text-xs text-muted-foreground">
                      {customer.code} · {customer.city}, {customer.stateName}
                      {customer.gstin ? ` · ${customer.gstin}` : ` · ${t('unregistered')}`}
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
