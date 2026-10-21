import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { PageHeader } from '@/components/PageHeader'
import { ActiveBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { errorMessage } from '@/lib/api/errors'
import { msg, t } from '@/lib/i18n'
import { formatPercent } from '@/lib/format'
import { useCategories, useHsnCodes } from '@/lib/lookups'
import { useDeleteCategory, useDeleteHsnCode, type Category, type HsnCode } from './api'
import { CategoryDialog, HsnCodeDialog } from './CatalogDialogs'

function RowActions({ onEdit, onDelete, canDelete, label }: { onEdit: () => void; onDelete: () => void; canDelete: boolean; label: string }) {
  return (
    <div className="flex justify-end gap-1">
      <Button variant="ghost" size="icon-sm" onClick={onEdit} aria-label={t('Edit {name}', { name: label })}>
        <Pencil />
      </Button>
      <Button
        variant="ghost"
        size="icon-sm"
        onClick={onDelete}
        disabled={!canDelete}
        aria-label={t('Delete {name}', { name: label })}
        title={canDelete ? undefined : t('In use by products. Mark it inactive instead.')}
      >
        <Trash2 />
      </Button>
    </div>
  )
}

export function CatalogPage() {
  const { can } = useAuth()
  const canEdit = can(Permissions.CatalogEdit)
  const categories = useCategories(true)
  const hsnCodes = useHsnCodes(true)
  const deleteCategory = useDeleteCategory()
  const deleteHsn = useDeleteHsnCode()

  const [categoryDialog, setCategoryDialog] = useState<{ open: boolean; item: Category | null }>({ open: false, item: null })
  const [hsnDialog, setHsnDialog] = useState<{ open: boolean; item: HsnCode | null }>({ open: false, item: null })
  const [pendingDelete, setPendingDelete] = useState<{ kind: 'category'; item: Category } | { kind: 'hsn'; item: HsnCode } | null>(null)

  const categoryColumns: Column<Category>[] = [
    { key: 'name', header: msg('Name'), cell: (c) => <span className="font-medium">{c.name}</span> },
    { key: 'description', header: msg('Description'), cell: (c) => c.description ?? '—' },
    { key: 'products', header: msg('Products'), align: 'right', cell: (c) => c.productCount },
    { key: 'status', header: msg('Status'), cell: (c) => <ActiveBadge active={c.isActive} /> },
  ]
  if (canEdit)
    categoryColumns.push({
      key: 'actions',
      header: '',
      className: 'w-24',
      cell: (c) => (
        <RowActions
          label={c.name}
          canDelete={c.productCount === 0}
          onEdit={() => setCategoryDialog({ open: true, item: c })}
          onDelete={() => setPendingDelete({ kind: 'category', item: c })}
        />
      ),
    })

  const hsnColumns: Column<HsnCode>[] = [
    { key: 'code', header: 'HSN', cell: (h) => <span className="font-mono">{h.code}</span>, className: 'w-24' },
    { key: 'description', header: msg('Description'), cell: (h) => h.description },
    { key: 'rate', header: 'GST', align: 'right', cell: (h) => formatPercent(h.gstRate) },
    { key: 'products', header: msg('Products'), align: 'right', cell: (h) => h.productCount },
    { key: 'status', header: msg('Status'), cell: (h) => <ActiveBadge active={h.isActive} /> },
  ]
  if (canEdit)
    hsnColumns.push({
      key: 'actions',
      header: '',
      className: 'w-24',
      cell: (h) => (
        <RowActions
          label={`HSN ${h.code}`}
          canDelete={h.productCount === 0}
          onEdit={() => setHsnDialog({ open: true, item: h })}
          onDelete={() => setPendingDelete({ kind: 'hsn', item: h })}
        />
      ),
    })

  const confirmDelete = async () => {
    if (!pendingDelete) return
    try {
      if (pendingDelete.kind === 'category') {
        await deleteCategory.mutateAsync(pendingDelete.item.id)
        toast.success(t('Category "{name}" deleted', { name: pendingDelete.item.name }))
      } else {
        await deleteHsn.mutateAsync(pendingDelete.item.id)
        toast.success(t('HSN {code} deleted', { code: pendingDelete.item.code }))
      }
    } catch (error) {
      toast.error(errorMessage(error))
    } finally {
      setPendingDelete(null)
    }
  }

  return (
    <>
      <PageHeader title={t('Categories & HSN codes')} description={t('Reference data for products. HSN codes carry the GST rate used on invoices.')} />

      <Tabs defaultValue="categories">
        <TabsList>
          <TabsTrigger value="categories">{t('Categories')}</TabsTrigger>
          <TabsTrigger value="hsn">{t('HSN codes & GST rates')}</TabsTrigger>
        </TabsList>

        <TabsContent value="categories" className="flex flex-col gap-4 pt-2">
          {canEdit && (
            <div className="flex justify-end">
              <Button onClick={() => setCategoryDialog({ open: true, item: null })}>
                <Plus />
                {t('New category')}
              </Button>
            </div>
          )}
          <DataTable columns={categoryColumns} rows={categories.data} getRowKey={(c) => c.id} isLoading={categories.isPending} error={categories.error} />
        </TabsContent>

        <TabsContent value="hsn" className="flex flex-col gap-4 pt-2">
          <div className="flex items-center justify-between gap-4">
            <p className="text-sm text-muted-foreground">{t('Starter rates are illustrative. Confirm current rates with your tax advisor.')}</p>
            {canEdit && (
              <Button onClick={() => setHsnDialog({ open: true, item: null })}>
                <Plus />
                {t('New HSN code')}
              </Button>
            )}
          </div>
          <DataTable columns={hsnColumns} rows={hsnCodes.data} getRowKey={(h) => h.id} isLoading={hsnCodes.isPending} error={hsnCodes.error} />
        </TabsContent>
      </Tabs>

      <CategoryDialog
        open={categoryDialog.open}
        item={categoryDialog.item}
        onOpenChange={(open) => setCategoryDialog((d) => ({ ...d, open }))}
      />
      <HsnCodeDialog open={hsnDialog.open} item={hsnDialog.item} onOpenChange={(open) => setHsnDialog((d) => ({ ...d, open }))} />
      <ConfirmDialog
        open={pendingDelete !== null}
        onOpenChange={(open) => !open && setPendingDelete(null)}
        title={t('Delete permanently?')}
        description={
          pendingDelete?.kind === 'category'
            ? t('Category "{name}" will be removed.', { name: pendingDelete.item.name })
            : t('HSN {code} will be removed.', { code: pendingDelete?.item.code ?? '' })
        }
        confirmLabel={t('Delete')}
        destructive
        onConfirm={() => void confirmDelete()}
      />
    </>
  )
}
