import { zodResolver } from '@hookform/resolvers/zod'
import { ChartNoAxesCombined } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation } from 'react-router'
import { z } from 'zod'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { useAuth } from './auth-context'
import { authStore } from './auth-store'
import { landingPage } from './sign-in-state'

const loginSchema = z.object({
  email: z.email({ error: 'Enter a valid email address.' }),
  password: z.string().min(1, { error: 'Enter your password.' }),
})

type LoginValues = z.infer<typeof loginSchema>

export function LoginPage() {
  const { status, user, login } = useAuth()
  const location = useLocation()

  // Read once on mount: was the user sent here because their session expired?
  const [expiredNotice] = useState(() => authStore.takeSignOutReason() === 'expired')
  const [serverError, setServerError] = useState<string | null>(null)

  const form = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  // Signing in lands here too: the page re-renders as signed in, so this is the one place that picks the next page.
  if (status === 'authenticated' && user) {
    return <Navigate to={landingPage(location.state, user.id)} replace />
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setServerError(null)
    try {
      await login(values.email, values.password)
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['email', 'password'])) {
        setServerError(errorMessage(error))
      }
    }
  })

  const { errors, isSubmitting } = form.formState

  return (
    <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <div className="flex w-full max-w-sm flex-col gap-6">
        <div className="flex items-center justify-center gap-2 text-lg font-semibold">
          <span className="flex size-8 items-center justify-center rounded-md bg-primary text-primary-foreground">
            <ChartNoAxesCombined className="size-4" />
          </span>
          SalesApp
        </div>

        <Card>
          <CardHeader className="text-center">
            <CardTitle className="text-xl">Sign in</CardTitle>
            <CardDescription>Use the account your administrator created for you.</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={onSubmit} noValidate>
              <FieldGroup>
                {expiredNotice && !serverError && (
                  <Alert>
                    <AlertDescription>Your session expired. Please sign in again.</AlertDescription>
                  </Alert>
                )}
                {serverError && (
                  <Alert variant="destructive" role="alert">
                    <AlertDescription>{serverError}</AlertDescription>
                  </Alert>
                )}

                <Field data-invalid={!!errors.email}>
                  <FieldLabel htmlFor="email">Email</FieldLabel>
                  <Input
                    id="email"
                    type="email"
                    autoComplete="username"
                    autoFocus
                    aria-invalid={!!errors.email}
                    {...form.register('email')}
                  />
                  <FieldError errors={[errors.email]} />
                </Field>

                <Field data-invalid={!!errors.password}>
                  <FieldLabel htmlFor="password">Password</FieldLabel>
                  <Input
                    id="password"
                    type="password"
                    autoComplete="current-password"
                    aria-invalid={!!errors.password}
                    {...form.register('password')}
                  />
                  <FieldError errors={[errors.password]} />
                </Field>

                <Button type="submit" className="w-full" disabled={isSubmitting}>
                  {isSubmitting && <Spinner />}
                  Sign in
                </Button>
              </FieldGroup>
            </form>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}
