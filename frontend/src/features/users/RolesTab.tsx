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
import { useDeleteRole, usePermissionCatalog, useRoles, useSaveRole, type Role } from './api'
import { PermissionGrid } from './PermissionGrid'

const roleSchema = z.object({
  name: requiredText('Name', 50).refine((v) => v.trim().length >= 2, { error: 'Use at least 2 characters.' }),
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
      toast.success(role ? `${saved.name} updated` : `${saved.name} created`, {
        description: role && saved.userCount > 0 ? `The change applies to its ${saved.userCount} users straight away.` : undefined,
      })
      onDone()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['name', 'description'])) toast.error(errorMessage(error))
    }
  })

  const title = role ? (readOnly ? role.name : `Edit ${role.name}`) : 'New role'
  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>
          {role?.isSystem
            ? 'Administrators can do everything. This role can’t be changed, and there is always at least one active administrator.'
            : 'A role is a set of permissions. Users get their role’s permissions, and you can still add or take away individual ones per user.'}
        </DialogDescription>
      </DialogHeader>
      <FieldGroup className="py-4">
        <fieldset disabled={readOnly} className="contents">
          <FormInput
            control={form.control}
            name="name"
            label="Name"
            autoFocus={!role}
            disabled={readOnly || role?.isBuiltIn}
            description={role?.isBuiltIn && !role.isSystem ? 'Built-in roles keep their names.' : undefined}
          />
          <FormTextarea control={form.control} name="description" label="Description" rows={2} description="Optional: who this role is for." />
        </fieldset>
        {catalog.data ? <PermissionGrid catalog={catalog.data} value={permissions} onChange={readOnly ? undefined : setPermissions} /> : <Spinner />}
      </FieldGroup>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {readOnly ? 'Close' : 'Cancel'}
        </Button>
        {!readOnly && (
          <Button type="submit" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting && <Spinner />}
            {role ? 'Save' : 'Create role'}
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
      header: 'Role',
      cell: (r) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{r.name}</span>
          {r.isBuiltIn && <Badge variant="secondary">Built-in</Badge>}
        </div>
      ),
    },
    { key: 'description', header: 'Description', cell: (r) => r.description ?? '—', className: 'whitespace-normal' },
    { key: 'permissions', header: 'Permissions', align: 'right', cell: (r) => (r.isSystem ? 'All' : r.permissions.length) },
    { key: 'users', header: 'Users', align: 'right', cell: (r) => r.userCount },
    {
      key: 'actions',
      header: '',
      className: 'w-24',
      cell: (r) => (
        <div className="flex justify-end gap-1">
          <Button variant="ghost" size="icon-sm" onClick={() => setEditing(r)} aria-label={canEdit && !r.isSystem ? `Edit ${r.name}` : `View ${r.name}`}>
            <Pencil />
          </Button>
          {canEdit && (
            <Button
              variant="ghost"
              size="icon-sm"
              disabled={r.isBuiltIn || r.userCount > 0}
              onClick={() => setDeleting(r)}
              aria-label={`Delete ${r.name}`}
              title={r.isBuiltIn ? 'Built-in roles can’t be deleted.' : r.userCount > 0 ? 'Give its users another role first.' : undefined}
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
      toast.success(`${deleting.name} deleted`)
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
        emptyTitle="No roles"
      />
      <RoleDialog open={creating || editing !== null} onOpenChange={(open) => !open && (setEditing(null), onCreatingChange(false))} role={editing} canEdit={canEdit} />
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? 'this role'}?`}
        description="Nobody has this role, so nothing else changes."
        confirmLabel="Delete"
        destructive
        onConfirm={() => void confirmDelete()}
      />
    </>
  )
}
