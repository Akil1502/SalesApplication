import { zodResolver } from '@hookform/resolvers/zod'
import { Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { FormInput, FormTextarea } from '@/components/form/fields'
import { requiredText } from '@/components/form/schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { roleLabel, useDeleteRole, usePermissionCatalog, useRoles, useSaveRole, type Role } from './api'
import { PermissionGrid } from './PermissionGrid'
import { msg, t } from '@/lib/i18n'

const roleSchema = z.object({
  name: requiredText(msg('Name'), 50).refine((v) => v.trim().length >= 2, { error: msg('Use at least 2 characters.') }),
  description: z.string().max(200),
})
type RoleValues = z.infer<typeof roleSchema>

/** Creates a role, or shows and changes one. Administrator is shown read-only; built-in roles keep their names. */
function RoleDialog({ open, onOpenChange, role, canEdit }: { open: boolean; onOpenChange: (open: boolean) => void; role: Role | null; canEdit: boolean }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-2xl">
        {/* The form mounts each time the dialog opens, so it starts from the role as saved. */}
        <RoleForm role={role} canEdit={canEdit} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function RoleForm({ role, canEdit, onDone }: { role: Role | null; canEdit: boolean; onDone: () => void }) {
  const catalog = usePermissionCatalog()
  const save = useSaveRole()
  const [permissions, setPermissions] = useState<Set<string>>(() => new Set(role?.permissions ?? []))
  const form = useForm<RoleValues>({ resolver: zodResolver(roleSchema), defaultValues: { name: role?.name ?? '', description: role?.description ?? '' } })
  const readOnly = !canEdit || !!role?.isSystem

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const saved = await save.mutateAsync({ id: role?.id ?? null, body: { ...values, permissions: [...permissions] } })
      toast.success(role ? t('{name} updated', { name: roleLabel(saved.name) }) : t('{name} created', { name: saved.name }), {
        description: role && saved.userCount > 0 ? t('The change applies to its {count} users straight away.', { count: saved.userCount }) : undefined,
      })
      onDone()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['name', 'description'])) toast.error(errorMessage(error))
    }
  })

  const title = role ? (readOnly ? roleLabel(role.name) : t('Edit {name}', { name: roleLabel(role.name) })) : t('New role')
  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>
          {role?.isSystem
            ? t('Administrators can do everything. This role can’t be changed, and there is always at least one active administrator.')
            : t('A role is a set of permissions. Users get their role’s permissions, and you can still add or take away individual ones per user.')}
        </DialogDescription>
      </DialogHeader>
      <FieldGroup className="py-4">
        <fieldset disabled={readOnly} className="contents">
          <FormInput
            control={form.control}
            name="name"
            label={t('Name')}
            autoFocus={!role}
            disabled={readOnly || role?.isBuiltIn}
            description={role?.isBuiltIn && !role.isSystem ? t('Built-in roles keep their names.') : undefined}
          />
          <FormTextarea control={form.control} name="description" label={t('Description')} rows={2} description={t('Optional: who this role is for.')} />
        </fieldset>
        {catalog.data ? <PermissionGrid catalog={catalog.data} value={permissions} onChange={readOnly ? undefined : setPermissions} /> : <Spinner />}
      </FieldGroup>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {readOnly ? t('Close') : t('Cancel')}
        </Button>
        {!readOnly && (
          <Button type="submit" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting && <Spinner />}
            {role ? t('Save') : t('Create role')}
          </Button>
        )}
      </DialogFooter>
    </form>
  )
}

export function RolesTab({ canEdit, creating, onCreatingChange }: { canEdit: boolean; creating: boolean; onCreatingChange: (open: boolean) => void }) {
  const roles = useRoles()
  const remove = useDeleteRole()
  const [editing, setEditing] = useState<Role | null>(null)
  const [deleting, setDeleting] = useState<Role | null>(null)

  const columns: Column<Role>[] = [
    {
      key: 'name',
      header: msg('Role'),
      cell: (r) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{roleLabel(r.name)}</span>
          {r.isBuiltIn && <Badge variant="secondary">{t('Built-in')}</Badge>}
        </div>
      ),
    },
    { key: 'description', header: msg('Description'), cell: (r) => r.description ?? '—', className: 'whitespace-normal' },
    { key: 'permissions', header: msg('Permissions'), align: 'right', cell: (r) => (r.isSystem ? t('All') : r.permissions.length) },
    { key: 'users', header: msg('Users'), align: 'right', cell: (r) => r.userCount },
    {
      key: 'actions',
      header: '',
      className: 'w-24',
      cell: (r) => (
        <div className="flex justify-end gap-1">
          <Button variant="ghost" size="icon-sm" onClick={() => setEditing(r)} aria-label={canEdit && !r.isSystem ? t('Edit {name}', { name: roleLabel(r.name) }) : t('View {name}', { name: roleLabel(r.name) })}>
            <Pencil />
          </Button>
          {canEdit && (
            <Button
              variant="ghost"
              size="icon-sm"
              disabled={r.isBuiltIn || r.userCount > 0}
              onClick={() => setDeleting(r)}
              aria-label={t('Delete {name}', { name: roleLabel(r.name) })}
              title={r.isBuiltIn ? t('Built-in roles can’t be deleted.') : r.userCount > 0 ? t('Give its users another role first.') : undefined}
            >
              <Trash2 />
            </Button>
          )}
        </div>
      ),
    },
  ]

  async function confirmDelete() {
    if (!deleting) return
    try {
      await remove.mutateAsync(deleting.id)
      toast.success(t('{name} deleted', { name: deleting.name }))
    } catch (error) {
      toast.error(errorMessage(error))
    } finally {
      setDeleting(null)
    }
  }

  return (
    <>
      <DataTable
        columns={columns}
        rows={roles.data}
        getRowKey={(r) => r.id}
        isLoading={roles.isPending}
        error={roles.error}
        onRowClick={(r) => setEditing(r)}
        emptyTitle={t('No roles')}
      />
      <RoleDialog open={creating || editing !== null} onOpenChange={(open) => !open && (setEditing(null), onCreatingChange(false))} role={editing} canEdit={canEdit} />
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={deleting ? t('Delete {name}?', { name: deleting.name }) : t('Delete this role?')}
        description={t('Nobody has this role, so nothing else changes.')}
        confirmLabel={t('Delete')}
        destructive
        onConfirm={() => void confirmDelete()}
      />
    </>
  )
}
