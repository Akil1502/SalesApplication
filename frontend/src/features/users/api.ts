import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type Schemas } from '@/lib/api/client'
import { unwrap } from '@/lib/api/errors'
import { currentUserQueryKey } from '@/features/auth/auth-context'
import { lookupKeys } from '@/lib/lookups'

export type User = Schemas['UserDto']
export type UserCreateRequest = Schemas['UserCreateRequest']
export type UserUpdateRequest = Schemas['UserUpdateRequest']

export const roleOptions = [
  { value: 'Admin', label: 'Administrator', description: 'Everything, including users and company settings.' },
  { value: 'SalesManager', label: 'Sales manager', description: 'All sales data, products, credit terms, margins and AI insights.' },
  { value: 'SalesRep', label: 'Sales representative', description: 'Customers, orders and invoices. No cost prices or credit terms.' },
] as const

export const roleLabel = (role: string) => roleOptions.find((r) => r.value === role)?.label ?? role

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

export function useResetPassword() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, newPassword }: { id: string; newPassword: string }) =>
      unwrap(api.POST('/api/users/{id}/reset-password', { params: { path: { id } }, body: { newPassword } })),
    onSuccess: invalidate,
  })
}
