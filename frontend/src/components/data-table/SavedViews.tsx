import { Bookmark, BookmarkPlus, Check, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { t } from '@/lib/i18n'
import { useSavedViews, viewSearch } from './table-settings'

/**
 * Named views of a list (its filters, search and sort), kept per person on the server: "Overdue, Tiruppur" is one
 * click away instead of three filters.
 */
export function SavedViews({ listKey }: { listKey: string }) {
  const location = useLocation()
  const navigate = useNavigate()
  const [views, setViews] = useSavedViews(listKey)
  const [naming, setNaming] = useState(false)
  const [name, setName] = useState('')
  const current = viewSearch(location.search)
  const active = views.find((view) => view.search === current)

  const save = () => {
    const trimmed = name.trim()
    if (!trimmed) return
    setViews((list) => [...list.filter((view) => view.name.toLowerCase() !== trimmed.toLowerCase()), { id: crypto.randomUUID(), name: trimmed, search: current }])
    setNaming(false)
    setName('')
    toast.success(t('Saved the view "{name}"', { name: trimmed }))
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className="bg-card">
            <Bookmark className={active ? 'fill-current text-brand' : undefined} />
            {active ? active.name : t('Views')}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="min-w-60">
          <DropdownMenuLabel className="text-xs text-muted-foreground">{t('Saved views')}</DropdownMenuLabel>
          {views.length === 0 && <p className="px-2 pb-2 text-xs text-muted-foreground">{t('Set the filters you use often, then save them as a view.')}</p>}
          {views.map((view) => (
            <DropdownMenuItem key={view.id} className="group/view" onSelect={() => navigate({ pathname: location.pathname, search: view.search })}>
              <Check className={view.id === active?.id ? 'opacity-100' : 'opacity-0'} />
              <span className="flex-1 truncate">{view.name}</span>
              <button
                type="button"
                className="rounded p-0.5 text-muted-foreground opacity-0 group-hover/view:opacity-100 group-focus/view:opacity-100 hover:text-destructive focus-visible:opacity-100"
                aria-label={t('Delete the view {name}', { name: view.name })}
                onClick={(event) => {
                  event.preventDefault()
                  event.stopPropagation()
                  setViews((list) => list.filter((v) => v.id !== view.id))
                }}
              >
                <Trash2 className="size-3.5" />
              </button>
            </DropdownMenuItem>
          ))}
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setNaming(true)} disabled={!current}>
            <BookmarkPlus />
            {t('Save the current filters…')}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={naming} onOpenChange={setNaming}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>{t('Save this view')}</DialogTitle>
            <DialogDescription>{t("The list's filters, search and sort, under a name. Only you see your views.")}</DialogDescription>
          </DialogHeader>
          <form
            onSubmit={(event) => {
              event.preventDefault()
              save()
            }}
            className="grid gap-2"
          >
            <Label htmlFor="view-name">{t('Name')}</Label>
            <Input id="view-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={40} placeholder={t('e.g. Overdue, Tiruppur')} autoFocus />
            <DialogFooter className="mt-2">
              <Button type="button" variant="outline" onClick={() => setNaming(false)}>
                {t('Cancel')}
              </Button>
              <Button type="submit" disabled={!name.trim()}>
                {t('Save view')}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  )
}
