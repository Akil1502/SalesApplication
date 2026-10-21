import { zodResolver } from '@hookform/resolvers/zod'
import { BrainCircuit, ChartNoAxesCombined, ReceiptIndianRupee, Sparkles, Warehouse } from 'lucide-react'
import { motion } from 'motion/react'
import { useContext, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation } from 'react-router'
import { z } from 'zod'
import { PreferencesContext } from '@/components/fx/preferences-context'
import { TiltCard } from '@/components/fx/TiltCard'
import { TextileHero3D } from '@/components/three/scenes'
import { BrandMark } from '@/components/layout/BrandMark'
import { LanguageSwitch } from '@/components/LanguageSwitch'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { brandName, useBranding, useDocumentTitle } from '@/features/branding/api'
import { applyFieldErrors, errorMessage } from '@/lib/api/errors'
import { msg, t } from '@/lib/i18n'
import { useAuth } from './auth-context'
import { authStore } from './auth-store'
import { landingPage } from './sign-in-state'

const loginSchema = z.object({
  email: z.email({ error: () => t('Enter a valid email address.') }),
  password: z.string().min(1, { error: () => t('Enter your password.') }),
})

type LoginValues = z.infer<typeof loginSchema>

const highlights = [
  { icon: ReceiptIndianRupee, text: msg('GST invoices, credit notes and payments') },
  { icon: Warehouse, text: msg('Stock, purchasing and supplier bills') },
  { icon: BrainCircuit, text: msg('Claude reads orders and answers questions') },
]

/** The still picture behind the headline when 3D is off: soft light and a few drawn yarn cones. */
function StillTextileArt() {
  const cone = (x: number, y: number, scale: number, from: string, to: string, tilt: number) => (
    <g transform={`translate(${x} ${y}) rotate(${tilt}) scale(${scale})`}>
      <defs>
        <linearGradient id={`cone-${x}`} x1="0" x2="1">
          <stop offset="0" stopColor={from} />
          <stop offset="1" stopColor={to} />
        </linearGradient>
      </defs>
      <path d="M-30 40 L-16 -40 L16 -40 L30 40 Z" fill={`url(#cone-${x})`} />
      <path d="M-26 22 L26 22 M-22 4 L22 4 M-19 -14 L19 -14" stroke="white" strokeOpacity="0.18" />
      <rect x="-5" y="-52" width="10" height="16" rx="3" fill="#c49f74" />
    </g>
  )
  return (
    <svg aria-hidden viewBox="0 0 600 600" preserveAspectRatio="xMidYMid slice" className="absolute inset-0 size-full opacity-80">
      {cone(170, 210, 1.6, '#f3eee4', '#cfc6b4', -12)}
      {cone(300, 330, 1.1, '#6f75ea', '#3b42b8', 18)}
      {cone(120, 420, 1.0, '#3db3c4', '#22808f', 26)}
      {cone(420, 170, 0.8, '#efb456', '#c98422', 10)}
      <g transform="rotate(-14 455 362)">
        <rect x="360" y="330" width="190" height="64" rx="32" fill="#3b5a8c" />
        <ellipse cx="392" cy="362" rx="20" ry="32" fill="#4d6ea3" stroke="white" strokeOpacity="0.15" />
      </g>
    </svg>
  )
}

export function LoginPage() {
  const { status, user, login } = useAuth()
  const location = useLocation()
  const branding = useBranding().data
  const name = brandName(branding)
  // The language switch needs the preferences (always there in the app; not in some tests).
  const hasPreferences = useContext(PreferencesContext) !== null
  useDocumentTitle(t('Sign in'))

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
    <main className="grid min-h-svh bg-background lg:grid-cols-[1.15fr_1fr]">
      {/* The showcase side: a 3D textile scene with the product's promise over it. */}
      <section
        className="relative hidden overflow-hidden text-white lg:block"
        style={{
          background:
            'radial-gradient(circle at 25% 20%, color-mix(in oklch, var(--brand) 70%, transparent), transparent 55%), radial-gradient(circle at 80% 85%, color-mix(in oklch, var(--brand-2) 45%, transparent), transparent 50%), linear-gradient(160deg, color-mix(in oklch, var(--sidebar) 85%, black), var(--sidebar) 50%, color-mix(in oklch, var(--sidebar) 70%, var(--brand)))',
        }}
      >
        <TextileHero3D className="absolute inset-0" fallback={<StillTextileArt />} />
        <div className="pointer-events-none relative z-10 flex h-full flex-col justify-between p-10 xl:p-14">
          <div className="flex items-center gap-3 text-lg font-semibold">
            {branding?.logoUrl ? (
              <BrandMark className="size-11" />
            ) : (
              <span className="flex size-9 items-center justify-center rounded-lg bg-white/15 ring-1 ring-white/25 backdrop-blur">
                <ChartNoAxesCombined className="size-5" />
              </span>
            )}
            {name}
          </div>

          <div className="max-w-md space-y-6">
            <motion.h2
              className="text-4xl leading-tight font-semibold tracking-tight text-balance"
              initial={{ opacity: 0, y: 24 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
            >
              {t('Sales, stock and GST billing, with AI built in.')}
            </motion.h2>
            <ul className="space-y-3">
              {highlights.map(({ icon: Icon, text }, i) => (
                <motion.li
                  key={text}
                  className="flex items-center gap-3 text-sm text-white/85"
                  initial={{ opacity: 0, x: -20 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ delay: 0.35 + i * 0.12, duration: 0.6, ease: [0.16, 1, 0.3, 1] }}
                >
                  <span className="flex size-8 items-center justify-center rounded-lg bg-white/10 ring-1 ring-white/20 backdrop-blur">
                    <Icon className="size-4" />
                  </span>
                  {t(text)}
                </motion.li>
              ))}
            </ul>
          </div>

          <p className="flex items-center gap-1.5 text-xs text-white/60">
            <Sparkles className="size-3.5" /> {t('Point at the scene: it follows you.')}
          </p>
        </div>
      </section>

      {/* The sign-in side. */}
      <section className="relative flex items-center justify-center overflow-hidden p-4 sm:p-8">
        {/* English or தமிழ் before signing in; carried into the account once signed in. */}
        {hasPreferences && (
          <div className="absolute top-4 right-4 z-10">
            <LanguageSwitch />
          </div>
        )}
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 -z-0 opacity-60 lg:opacity-40"
          style={{
            background:
              'radial-gradient(circle at 80% 10%, color-mix(in oklch, var(--brand) 18%, transparent), transparent 45%), radial-gradient(circle at 10% 90%, color-mix(in oklch, var(--brand-2) 16%, transparent), transparent 45%)',
          }}
        />
        <motion.div
          className="relative flex w-full max-w-sm flex-col gap-6"
          initial={{ opacity: 0, y: 30, rotateX: 12 }}
          animate={{ opacity: 1, y: 0, rotateX: 0 }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          style={{ transformPerspective: 1200 }}
        >
          <div className="flex items-center justify-center gap-2 text-lg font-semibold lg:hidden">
            <BrandMark className="size-9" />
            {name}
          </div>

          <TiltCard max={4}>
            <Card className="fx-glass border-0 shadow-[var(--shadow-lifted)]">
              <CardHeader className="text-center">
                <CardTitle className="text-2xl tracking-tight">{t('Welcome back')}</CardTitle>
                <CardDescription>{t('Sign in with the account your administrator created for you.')}</CardDescription>
              </CardHeader>
              <CardContent>
                <form onSubmit={onSubmit} noValidate>
                  <FieldGroup>
                    {expiredNotice && !serverError && (
                      <Alert>
                        <AlertDescription>{t('Your session expired. Please sign in again.')}</AlertDescription>
                      </Alert>
                    )}
                    {serverError && (
                      <Alert variant="destructive" role="alert">
                        <AlertDescription>{serverError}</AlertDescription>
                      </Alert>
                    )}

                    <Field data-invalid={!!errors.email}>
                      <FieldLabel htmlFor="email">{t('Email')}</FieldLabel>
                      <Input id="email" type="email" autoComplete="username" autoFocus aria-invalid={!!errors.email} {...form.register('email')} />
                      <FieldError errors={[errors.email]} />
                    </Field>

                    <Field data-invalid={!!errors.password}>
                      <FieldLabel htmlFor="password">{t('Password')}</FieldLabel>
                      <Input
                        id="password"
                        type="password"
                        autoComplete="current-password"
                        aria-invalid={!!errors.password}
                        {...form.register('password')}
                      />
                      <FieldError errors={[errors.password]} />
                    </Field>

                    <Button type="submit" size="lg" className="w-full" disabled={isSubmitting}>
                      {isSubmitting && <Spinner />}
                      {t('Sign in')}
                    </Button>
                  </FieldGroup>
                </form>
              </CardContent>
            </Card>
          </TiltCard>
        </motion.div>
      </section>
    </main>
  )
}
