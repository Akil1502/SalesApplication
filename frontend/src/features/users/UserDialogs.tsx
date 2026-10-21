import { zodResolver } from '@hookform/resolvers/zod'
import { ChevronDown, ChevronRight, RotateCcw } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormInput, FormSelect, FormSwitch } from '@/components/form/fields'
import { requiredText } from '@/components/form/schemas'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { roleLabel, useCreateUser, usePermissionCatalog, useResetPassword, useRoles, useUpdateUser, type Role, type User } from './api'
import { changesFrom, effective } from './permission-logic'
import { PermissionGrid } from './PermissionGrid'
import { msg, t, tn } from '@/lib/i18n'

// Matches the server's Identity password policy: 8+ characters with upper, lower and a digit.
const password = z
  .string()
  .min(8, { error: msg('Use at least 8 characters.') })
  .max(128)
  .regex(/[A-Z]/, { error: msg('Include an upper-case letter.') })
  .regex(/[a-z]/, { error: msg('Include a lower-case letter.') })
  .regex(/[0-9]/, { error: msg('Include a digit.') })

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

/** A user's permissions while the dialog is open, tied to the role they were set for; null means exactly the role's. */
type Granted = { role: string; permissions: Set<string> } | null

function roleSummary(role: Role | undefined) {
  if (!role) return undefined
  if (role.isSystem) return t('Every permission, always.')
  return role.description || tn(role.permissions.length, '{count} permission.', '{count} permissions.')
}

/** What to send: how the user's permissions differ from their role's (nothing when they match it). */
function changesToSave(role: Role | undefined, granted: Granted) {
  const rolePermissions = new Set(role?.permissions ?? [])
  return changesFrom(rolePermissions, granted && granted.role === role?.name ? granted.permissions : rolePermissions)
}

/**
 * The role's permissions for this user, and their own changes on top. Collapsed until opened unless the user already
 * has changes, since most users simply take their role's permissions.
 */
function UserPermissions({
  role,
  granted,
  onChange,
  readOnlyReason,
}: {
  role: Role | undefined
  granted: Granted
  onChange: (next: Granted) => void
  readOnlyReason?: string
}) {
  const catalog = usePermissionCatalog()
  const [expanded, setExpanded] = useState<boolean | null>(null)

  if (!role) return null
  if (role.isSystem) return <p className="text-sm text-muted-foreground">{t('Administrators can do everything, so there are no permissions to change.')}</p>

  const rolePermissions = new Set(role.permissions)
  const value = granted && granted.role === role.name ? granted.permissions : rolePermissions
  const { addedPermissions, removedPermissions } = changesFrom(rolePermissions, value)
  const changed = addedPermissions.length + removedPermissions.length > 0
  const open = expanded ?? changed
  const roleName = roleLabel(role.name)
  const added = addedPermissions.length
  const removed = removedPermissions.length
  const summary =
    added > 0 && removed > 0
      ? t('{added} added, {removed} taken away compared with the {role} role.', { added, removed, role: roleName })
      : added > 0
        ? t('{added} added compared with the {role} role.', { added, role: roleName })
        : removed > 0
          ? t('{removed} taken away compared with the {role} role.', { removed, role: roleName })
          : t('The same as the {role} role.', { role: roleName })

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button type="button" variant="ghost" size="sm" className="-ml-2" onClick={() => setExpanded(!open)} aria-expanded={open}>
          {open ? <ChevronDown /> : <ChevronRight />}
          {t('Permissions')}
        </Button>
        {changed && !readOnlyReason && (
          <Button type="button" variant="ghost" size="sm" onClick={() => onChange(null)}>
            <RotateCcw />
            {t('Reset to role')}
          </Button>
        )}
      </div>
      <p className="text-sm text-muted-foreground">
        {summary} {readOnlyReason}
      </p>
      {open &&
        (catalog.data ? (
          <PermissionGrid
            catalog={catalog.data}
            value={value}
            baseline={rolePermissions}
            onChange={readOnlyReason ? undefined : (next) => onChange({ role: role.name, permissions: next })}
          />
        ) : (
          <Spinner />
        ))}
    </div>
  )
}

function useRoleOptions() {
  const roles = useRoles()
  const list = roles.data ?? []
  return { roles: list, options: list.map((r) => ({ value: r.name, label: roleLabel(r.name) })) }
}

const createSchema = z.object({
  fullName: requiredText(msg('Full name'), 120),
  email: z.email({ error: msg('Enter a valid email address.') }),
  role: z.string().min(1, { error: msg('Choose a role.') }),
  password,
})
type CreateValues = z.infer<typeof createSchema>

const defaultRole = 'Sales representative'

export function CreateUserDialog({ open, onOpenChange }: DialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-2xl">
        {/* The form mounts each time the dialog opens, so it always starts empty. */}
        <CreateUserForm onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function CreateUserForm({ onDone }: { onDone: () => void }) {
  const create = useCreateUser()
  const { roles, options } = useRoleOptions()
  const [granted, setGranted] = useState<Granted>(null)
  const form = useForm<CreateValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { fullName: '', email: '', role: defaultRole, password: '' },
  })

  const roleName = useWatch({ control: form.control, name: 'role' })
  const role = roles.find((r) => r.name === roleName)

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const user = await create.mutateAsync({ ...values, ...changesToSave(roles.find((r) => r.name === values.role), granted) })
      toast.success(t('{name} can now sign in', { name: user.fullName }), { description: t('Share the initial password with them securely.') })
      onDone()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['fullName', 'email', 'role', 'password'])) toast.error(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>{t('Add user')}</DialogTitle>
        <DialogDescription>{t('They sign in with this email and the initial password you set.')}</DialogDescription>
      </DialogHeader>
      <FieldGroup className="py-4">
        <FormInput control={form.control} name="fullName" label={t('Full name')} autoFocus />
        <FormInput control={form.control} name="email" label={t('Email')} type="email" autoComplete="off" />
        <FormSelect control={form.control} name="role" label={t('Role')} options={options} description={roleSummary(role)} />
        <UserPermissions key={roleName} role={role} granted={granted} onChange={setGranted} />
        <FormInput
          control={form.control}
          name="password"
          label={t('Initial password')}
          type="password"
          autoComplete="new-password"
          description={t('At least 8 characters with upper and lower case letters and a digit.')}
        />
      </FieldGroup>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {t('Add user')}
        </Button>
      </DialogFooter>
    </form>
  )
}

const editSchema = z.object({
  fullName: requiredText(msg('Full name'), 120),
  role: z.string().min(1, { error: msg('Choose a role.') }),
  isActive: z.boolean(),
})
type EditValues = z.infer<typeof editSchema>

export function EditUserDialog({ open, onOpenChange, user, isSelf }: DialogProps & { user: User | null; isSelf: boolean }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-2xl">
        {/* The form mounts each time the dialog opens, so it starts from the user's saved details. */}
        {user && <EditUserForm user={user} isSelf={isSelf} onDone={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  )
}

function EditUserForm({ user, isSelf, onDone }: { user: User; isSelf: boolean; onDone: () => void }) {
  const update = useUpdateUser()
  const catalog = usePermissionCatalog()
  const { roles, options } = useRoleOptions()
  const form = useForm<EditValues>({
    resolver: zodResolver(editSchema),
    defaultValues: { fullName: user.fullName, role: user.role, isActive: user.isActive },
  })

  // What's saved: the role's permissions adjusted by the user's own changes. It needs the roles and the permission
  // rules, which may still be loading, so it's worked out on each render until the admin changes something.
  const savedRole = roles.find((r) => r.name === user.role)
  const saved: Granted =
    savedRole && catalog.data && user.addedPermissions.length + user.removedPermissions.length > 0
      ? { role: savedRole.name, permissions: effective(savedRole.permissions, user.addedPermissions, user.removedPermissions, catalog.data.needs) }
      : null
  const [edited, setEdited] = useState<Granted | undefined>(undefined)
  const granted = edited === undefined ? saved : edited

  const roleName = useWatch({ control: form.control, name: 'role' })
  const role = roles.find((r) => r.name === roleName)

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync({ id: user.id, body: { ...values, ...changesToSave(roles.find((r) => r.name === values.role), granted) } })
      toast.success(t('{name} updated', { name: values.fullName }))
      onDone()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['fullName', 'role', 'isActive'])) toast.error(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <DialogHeader>
        <DialogTitle>{t('Edit {name}', { name: user.fullName })}</DialogTitle>
        <DialogDescription>{user.email}</DialogDescription>
      </DialogHeader>
      <FieldGroup className="py-4">
        <FormInput control={form.control} name="fullName" label={t('Full name')} />
        <FormSelect
          control={form.control}
          name="role"
          label={t('Role')}
          options={options}
          disabled={isSelf}
          description={isSelf ? t("You can't change your own role.") : roleSummary(role)}
        />
        <UserPermissions
          key={roleName}
          role={role}
          granted={granted}
          onChange={setEdited}
          readOnlyReason={isSelf ? t("You can't change your own permissions.") : undefined}
        />
        <FormSwitch
          control={form.control}
          name="isActive"
          label={t('Active')}
          disabled={isSelf}
          description={isSelf ? t("You can't deactivate your own account.") : t('Inactive users can no longer sign in. Their history is kept.')}
        />
      </FieldGroup>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('Cancel')}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {t('Save')}
        </Button>
      </DialogFooter>
    </form>
  )
}

const resetSchema = z.object({ newPassword: password })
type ResetValues = z.infer<typeof resetSchema>

export function ResetPasswordDialog({ open, onOpenChange, user }: DialogProps & { user: User | null }) {
  const reset = useResetPassword()
  const form = useForm<ResetValues>({ resolver: zodResolver(resetSchema), defaultValues: { newPassword: '' } })

  useEffect(() => {
    if (open) form.reset({ newPassword: '' })
  }, [open, form])

  const onSubmit = form.handleSubmit(async (values) => {
    if (!user) return
    try {
      await reset.mutateAsync({ id: user.id, newPassword: values.newPassword })
      toast.success(t('Password reset for {name}', { name: user.fullName }), { description: t('Any lockout was cleared too.') })
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['newPassword'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{t('Reset password')}</DialogTitle>
            <DialogDescription>{t('Set a new password for {name}. This also unlocks a locked-out account.', { name: user?.fullName ?? '' })}</DialogDescription>
          </DialogHeader>
          <FieldGroup className="py-4">
            <FormInput control={form.control} name="newPassword" label={t('New password')} type="password" autoComplete="new-password" autoFocus />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              {t('Reset password')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
