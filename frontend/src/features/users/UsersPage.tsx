import { KeyRound, Lock, MoreHorizontal, Pencil, Plus } from 'lucide-react'
import { useState } from 'react'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Pagination } from '@/components/data-table/Pagination'
import { SearchInput } from '@/components/data-table/SearchInput'
import { useListParams } from '@/components/data-table/use-list-params'
import { PageHeader } from '@/components/PageHeader'
import { ActiveBadge } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { useAuth } from '@/features/auth/auth-context'
import { formatDate } from '@/lib/format'
import { roleLabel, useUsers, type User } from './api'
import { CreateUserDialog, EditUserDialog, ResetPasswordDialog } from './UserDialogs'

type DialogState = { kind: 'create' } | { kind: 'edit'; user: User } | { kind: 'reset'; user: User } | null

export function UsersPage() {
  const { user: me } = useAuth()
  const [params, update] = useListParams({ sort: 'fullName' })
  const [dialog, setDialog] = useState<DialogState>(null)

  const users = useUsers({
    q: params.q || undefined,
    page: params.page,
    pageSize: params.pageSize,
    sort: params.sort,
    includeInactive: params.includeInactive,
  })

  const columns: Column<User>[] = [
    {
      key: 'name',
      header: 'Name',
      sortKey: 'fullName',
      cell: (u) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{u.fullName}</span>
          {u.id === me?.id && <Badge variant="secondary">You</Badge>}
        </div>
      ),
    },
    { key: 'email', header: 'Email', sortKey: 'email', cell: (u) => u.email },
    { key: 'role', header: 'Role', sortKey: 'role', cell: (u) => roleLabel(u.role) },
    {
      key: 'status',
      header: 'Status',
      cell: (u) => (
        <div className="flex items-center gap-1.5">
          <ActiveBadge active={u.isActive} />
          {u.isLockedOut && (
            <Badge variant="outline" className="border-amber-600/30 text-amber-700 dark:text-amber-400">
              <Lock />
              Locked
            </Badge>
          )}
        </div>
      ),
    },
    { key: 'created', header: 'Added', sortKey: 'createdAt', cell: (u) => formatDate(u.createdAtUtc) },
    {
      key: 'actions',
      header: '',
      className: 'w-12',
      cell: (u) => (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon-sm" aria-label={`Actions for ${u.fullName}`}>
              <MoreHorizontal />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={() => setDialog({ kind: 'edit', user: u })}>
              <Pencil />
              Edit
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => setDialog({ kind: 'reset', user: u })}>
              <KeyRound />
              Reset password
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      ),
    },
  ]

  const close = (open: boolean) => !open && setDialog(null)

  return (
    <>
      <PageHeader
        title="Users"
        description="Who can sign in, and what they can do."
        actions={
          <Button onClick={() => setDialog({ kind: 'create' })}>
            <Plus />
            Add user
          </Button>
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder="Search name or email" />
        <div className="flex items-center gap-2">
          <Switch id="show-inactive" checked={params.includeInactive} onCheckedChange={(includeInactive) => update({ includeInactive })} />
          <Label htmlFor="show-inactive">Show inactive</Label>
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={users.data?.items}
        getRowKey={(u) => u.id}
        isLoading={users.isPending}
        error={users.error}
        sort={params.sort}
        onSortChange={(sort) => update({ sort })}
        emptyTitle="No users match"
      />

      {users.data && (
        <Pagination
          page={users.data.page}
          pageSize={users.data.pageSize}
          totalCount={users.data.totalCount}
          totalPages={users.data.totalPages ?? 0}
          onPageChange={(page) => update({ page })}
        />
      )}

      <CreateUserDialog open={dialog?.kind === 'create'} onOpenChange={close} />
      <EditUserDialog
        open={dialog?.kind === 'edit'}
        onOpenChange={close}
        user={dialog?.kind === 'edit' ? dialog.user : null}
        isSelf={dialog?.kind === 'edit' && dialog.user.id === me?.id}
      />
      <ResetPasswordDialog open={dialog?.kind === 'reset'} onOpenChange={close} user={dialog?.kind === 'reset' ? dialog.user : null} />
    </>
  )
}
