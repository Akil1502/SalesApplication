import { KeyRound, Lock, MoreHorizontal, Pencil, Plus } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { formatDate } from '@/lib/format'
import { roleLabel, useUsers, type User } from './api'
import { RolesTab } from './RolesTab'
import { CreateUserDialog, EditUserDialog, ResetPasswordDialog } from './UserDialogs'
import { msg, t } from '@/lib/i18n'

type DialogState = { kind: 'create' } | { kind: 'edit'; user: User } | { kind: 'reset'; user: User } | null

function UsersTab({ canEdit, dialog, setDialog }: { canEdit: boolean; dialog: DialogState; setDialog: (d: DialogState) => void }) {
  const { user: me } = useAuth()
  const [params, update] = useListParams({ sort: 'fullName' })

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
      header: msg('Name'),
      sortKey: 'fullName',
      cell: (u) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{u.fullName}</span>
          {u.id === me?.id && <Badge variant="secondary">{t('You')}</Badge>}
        </div>
      ),
    },
    { key: 'email', header: msg('Email'), sortKey: 'email', cell: (u) => u.email },
    {
      key: 'role',
      header: msg('Role'),
      sortKey: 'role',
      cell: (u) => {
        const added = u.addedPermissions.length
        const removed = u.removedPermissions.length
        return (
          <div className="flex items-center gap-1.5">
            <span>{roleLabel(u.role)}</span>
            {added + removed > 0 && (
              <Badge variant="outline" title={t('Their own permissions: {added} added to and {removed} taken from the role.', { added, removed })}>
                {[added > 0 && `+${added}`, removed > 0 && `−${removed}`].filter(Boolean).join(' ')}
              </Badge>
            )}
          </div>
        )
      },
    },
    {
      key: 'status',
      header: msg('Status'),
      cell: (u) => (
        <div className="flex items-center gap-1.5">
          <ActiveBadge active={u.isActive} />
          {u.isLockedOut && (
            <Badge variant="outline" className="border-amber-600/30 text-amber-700 dark:text-amber-400">
              <Lock />
              {t('Locked')}
            </Badge>
          )}
        </div>
      ),
    },
    { key: 'created', header: msg('Added'), sortKey: 'createdAt', cell: (u) => formatDate(u.createdAtUtc) },
  ]
  if (canEdit)
    columns.push({
      key: 'actions',
      header: '',
      className: 'w-12',
      cell: (u) => (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon-sm" aria-label={t('Actions for {name}', { name: u.fullName })}>
              <MoreHorizontal />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={() => setDialog({ kind: 'edit', user: u })}>
              <Pencil />
              {t('Edit')}
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => setDialog({ kind: 'reset', user: u })}>
              <KeyRound />
              {t('Reset password')}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      ),
    })

  const close = (open: boolean) => !open && setDialog(null)

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <SearchInput value={params.q} onChange={(q) => update({ q })} placeholder={t('Search name or email')} />
        <div className="flex items-center gap-2">
          <Switch id="show-inactive" checked={params.includeInactive} onCheckedChange={(includeInactive) => update({ includeInactive })} />
          <Label htmlFor="show-inactive">{t('Show inactive')}</Label>
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
        emptyTitle={t('No users match')}
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

export function UsersPage() {
  const { can } = useAuth()
  const canEdit = can(Permissions.UsersEdit)
  const [searchParams, setSearchParams] = useSearchParams()
  const tab = searchParams.get('tab') === 'roles' ? 'roles' : 'users'
  const [dialog, setDialog] = useState<DialogState>(null)
  const [creatingRole, setCreatingRole] = useState(false)

  return (
    <>
      <PageHeader
        title={t('Users & roles')}
        description={t('Who can sign in, and what they can do. A role is a set of permissions; each user can also have permissions added or taken away.')}
        actions={
          canEdit &&
          (tab === 'users' ? (
            <Button onClick={() => setDialog({ kind: 'create' })}>
              <Plus />
              {t('Add user')}
            </Button>
          ) : (
            <Button onClick={() => setCreatingRole(true)}>
              <Plus />
              {t('New role')}
            </Button>
          ))
        }
      />

      <Tabs value={tab} onValueChange={(next) => setSearchParams(next === 'roles' ? { tab: next } : {})}>
        <TabsList>
          <TabsTrigger value="users">{t('Users')}</TabsTrigger>
          <TabsTrigger value="roles">{t('Roles')}</TabsTrigger>
        </TabsList>
        <TabsContent value="users" className="flex flex-col gap-4 pt-2">
          {tab === 'users' && <UsersTab canEdit={canEdit} dialog={dialog} setDialog={setDialog} />}
        </TabsContent>
        <TabsContent value="roles" className="flex flex-col gap-4 pt-2">
          {tab === 'roles' && <RolesTab canEdit={canEdit} creating={creatingRole} onCreatingChange={setCreatingRole} />}
        </TabsContent>
      </Tabs>
    </>
  )
}
