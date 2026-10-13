import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect } from 'react'
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
import { roleOptions, useCreateUser, useResetPassword, useUpdateUser, type User } from './api'

// Matches the server's Identity password policy: 8+ characters with upper, lower and a digit.
const password = z
  .string()
  .min(8, { error: 'Use at least 8 characters.' })
  .max(128)
  .regex(/[A-Z]/, { error: 'Include an upper-case letter.' })
  .regex(/[a-z]/, { error: 'Include a lower-case letter.' })
  .regex(/[0-9]/, { error: 'Include a digit.' })

const roleSelectOptions = roleOptions.map((r) => ({ value: r.value, label: r.label }))
const roleDescription = (role: string) => roleOptions.find((r) => r.value === role)?.description

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

const createSchema = z.object({
  fullName: requiredText('Full name', 120),
  email: z.email({ error: 'Enter a valid email address.' }),
  role: z.string().min(1, { error: 'Choose a role.' }),
  password,
})
type CreateValues = z.infer<typeof createSchema>

export function CreateUserDialog({ open, onOpenChange }: DialogProps) {
  const create = useCreateUser()
  const form = useForm<CreateValues>({
    resolver: zodResolver(createSchema),
    defaultValues: { fullName: '', email: '', role: 'SalesRep', password: '' },
  })

  useEffect(() => {
    if (open) form.reset({ fullName: '', email: '', role: 'SalesRep', password: '' })
  }, [open, form])

  const role = useWatch({ control: form.control, name: 'role' })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const user = await create.mutateAsync(values)
      toast.success(`${user.fullName} can now sign in`, { description: 'Share the initial password with them securely.' })
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['fullName', 'email', 'role', 'password'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>Add user</DialogTitle>
            <DialogDescription>They sign in with this email and the initial password you set.</DialogDescription>
          </DialogHeader>
          <FieldGroup className="py-4">
            <FormInput control={form.control} name="fullName" label="Full name" autoFocus />
            <FormInput control={form.control} name="email" label="Email" type="email" autoComplete="off" />
            <FormSelect control={form.control} name="role" label="Role" options={roleSelectOptions} description={roleDescription(role)} />
            <FormInput
              control={form.control}
              name="password"
              label="Initial password"
              type="password"
              autoComplete="new-password"
              description="At least 8 characters with upper and lower case letters and a digit."
            />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              Add user
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const editSchema = z.object({
  fullName: requiredText('Full name', 120),
  role: z.string().min(1, { error: 'Choose a role.' }),
  isActive: z.boolean(),
})
type EditValues = z.infer<typeof editSchema>

export function EditUserDialog({ open, onOpenChange, user, isSelf }: DialogProps & { user: User | null; isSelf: boolean }) {
  const update = useUpdateUser()
  const form = useForm<EditValues>({ resolver: zodResolver(editSchema), defaultValues: { fullName: '', role: '', isActive: true } })

  useEffect(() => {
    if (open && user) form.reset({ fullName: user.fullName, role: user.role, isActive: user.isActive })
  }, [open, user, form])

  const role = useWatch({ control: form.control, name: 'role' })

  const onSubmit = form.handleSubmit(async (values) => {
    if (!user) return
    try {
      await update.mutateAsync({ id: user.id, body: values })
      toast.success(`${values.fullName} updated`)
      onOpenChange(false)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['fullName', 'role', 'isActive'])) toast.error(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>Edit {user?.fullName}</DialogTitle>
            <DialogDescription>{user?.email}</DialogDescription>
          </DialogHeader>
          <FieldGroup className="py-4">
            <FormInput control={form.control} name="fullName" label="Full name" />
            <FormSelect
              control={form.control}
              name="role"
              label="Role"
              options={roleSelectOptions}
              disabled={isSelf}
              description={isSelf ? "You can't change your own role." : roleDescription(role)}
            />
            <FormSwitch
              control={form.control}
              name="isActive"
              label="Active"
              disabled={isSelf}
              description={isSelf ? "You can't deactivate your own account." : 'Inactive users can no longer sign in. Their history is kept.'}
            />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              Save
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
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
      toast.success(`Password reset for ${user.fullName}`, { description: 'Any lockout was cleared too.' })
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
            <DialogTitle>Reset password</DialogTitle>
            <DialogDescription>Set a new password for {user?.fullName}. This also unlocks a locked-out account.</DialogDescription>
          </DialogHeader>
          <FieldGroup className="py-4">
            <FormInput control={form.control} name="newPassword" label="New password" type="password" autoComplete="new-password" autoFocus />
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              Reset password
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
