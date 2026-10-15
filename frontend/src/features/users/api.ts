import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { currentUserQueryKey } from '@/features/auth/auth-context'
import { lookupKeys } from '@/lib/lookups'

export type User = Schemas['UserDto']
export type UserCreateRequest = Schemas['UserCreateRequest']
export type UserUpdateRequest = Schemas['UserUpdateRequest']

export type Role = Schemas['RoleDto']
export type RoleSaveRequest = Schemas['RoleSaveRequest']
export type PermissionCatalog = Schemas['PermissionCatalogDto']

export interface UserListQuery {
  q?: string
  page: number
  pageSize: number
  sort?: string
  includeInactive?: boolean
  role?: string
}

export function useUsers(query: UserListQuery) {
  return useQuery({
    queryKey: ['users', 'list', query],
    queryFn: () => unwrap(api.GET('/api/users', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

function useInvalidateUsers() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['users'] })
    void queryClient.invalidateQueries({ queryKey: ['roles'] })
    void queryClient.invalidateQueries({ queryKey: lookupKeys.salesReps })
    // The signed-in admin may have edited their own name.
    void queryClient.invalidateQueries({ queryKey: currentUserQueryKey })
  }
}

export function useCreateUser() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: (body: UserCreateRequest) => unwrap(api.POST('/api/users', { body })),
    onSuccess: invalidate,
  })
}

export function useUpdateUser() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UserUpdateRequest }) =>
      unwrap(api.PUT('/api/users/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  })
}

export function useRoles() {
  return useQuery({ queryKey: ['roles', 'list'], queryFn: () => unwrap(api.GET('/api/roles')) })
}

/** The permissions to choose from; fixed for a given server version, so fetched once. */
export function usePermissionCatalog() {
  return useQuery({ queryKey: ['roles', 'permissions'], queryFn: () => unwrap(api.GET('/api/roles/permissions')), staleTime: Infinity })
}

function useInvalidateRoles() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['roles'] })
    void queryClient.invalidateQueries({ queryKey: ['users'] })
    void queryClient.invalidateQueries({ queryKey: lookupKeys.salesReps })
    // The signed-in user's own role may have changed.
    void queryClient.invalidateQueries({ queryKey: currentUserQueryKey })
  }
}

export function useSaveRole() {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: ({ id, body }: { id: string | null; body: RoleSaveRequest }) =>
      id ? unwrap(api.PUT('/api/roles/{id}', { params: { path: { id } }, body })) : unwrap(api.POST('/api/roles', { body })),
    onSuccess: invalidate,
  })
}

export function useDeleteRole() {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/roles/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  })
}

export function useResetPassword() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, newPassword }: { id: string; newPassword: string }) =>
      unwrap(api.POST('/api/users/{id}/reset-password', { params: { path: { id } }, body: { newPassword } })),
    onSuccess: invalidate,
  })
}
