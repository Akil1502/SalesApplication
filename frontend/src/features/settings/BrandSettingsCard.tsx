import { ChartNoAxesCombined, ImagePlus, Paintbrush, RotateCcw, Trash2, Upload } from 'lucide-react'
import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { useBranding, useRemoveLogo, useSetLogo } from '@/features/branding/api'
import { previewBrandColour } from '@/features/branding/apply'
import { brandTheme, DEFAULT_BRAND_COLOUR } from '@/features/branding/brand-theme'
import { errorMessage } from '@/lib/api/errors'
import { t } from '@/lib/i18n'
import { cn } from '@/lib/utils'

const isColour = (value: string) => /^#[0-9a-f]{6}$/i.test(value)

/** The logo on the two backgrounds it appears on: light pages and printed documents, and the dark menu. */
function LogoPreview({ logoUrl, busy }: { logoUrl: string | null | undefined; busy: boolean }) {
  return (
    <div className="grid grid-cols-2 overflow-hidden rounded-xl border">
      {(['bg-white', 'bg-sidebar'] as const).map((background) => (
        <div key={background} className={cn('relative flex h-28 items-center justify-center p-4', background)}>
          {logoUrl ? (
            <span className="flex size-20 items-center justify-center rounded-xl bg-white p-2 shadow-sm">
              <img src={logoUrl} alt={t('Company logo')} className="size-full object-contain" />
            </span>
          ) : (
            <span className="flex size-12 items-center justify-center rounded-xl text-white" style={{ background: 'var(--gradient-brand)' }}>
              <ChartNoAxesCombined className="size-6" />
            </span>
          )}
          {busy && (
            <span className="absolute inset-0 flex items-center justify-center bg-background/60 backdrop-blur-sm" role="status">
              <Spinner />
            </span>
          )}
        </div>
      ))}
    </div>
  )
}

/**
 * The company's logo and colour. The logo is saved the moment it's chosen (it has its own place on the server);
 * the colour is part of the settings form, previewed live across the whole app until saved or discarded.
 */
export function BrandSettingsCard<T extends FieldValues>({ control, name }: { control: Control<T>; name: Path<T> }) {
  const branding = useBranding().data
  const setLogo = useSetLogo()
  const removeLogo = useRemoveLogo()
  const busy = setLogo.isPending || removeLogo.isPending
  const input = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)
  const colourId = useId()

  const {
    field: { ref: fieldRef, ...field },
    fieldState,
  } = useController({ control, name })
  const colour = typeof field.value === 'string' ? field.value : ''
  const theme = useMemo(() => brandTheme(isColour(colour) ? colour : DEFAULT_BRAND_COLOUR), [colour])

  // Try the colour on the whole app while choosing; leaving the page puts the saved one back.
  useEffect(() => {
    previewBrandColour(isColour(colour) ? colour : colour === '' ? null : undefined)
  }, [colour])
  useEffect(() => () => previewBrandColour(undefined), [])

  const upload = (file: File | undefined) => {
    if (!file) return
    const replacing = !!branding?.logoUrl
    setLogo.mutate(file, {
      onSuccess: () => toast.success(replacing ? t('Logo replaced') : t('Logo added')),
      onError: (error) => toast.error(errorMessage(error)),
    })
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Logo and colour')}</CardTitle>
        <CardDescription>
          {t(
            'The logo appears in the menu, on the sign-in page, in the browser tab and on printed documents, and the company\'s name then replaces "SalesApp". The colour is used for buttons, the menu and highlights for everyone.',
          )}
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-6 md:grid-cols-2">
        <div
          className={cn('flex flex-col gap-3 rounded-xl transition-shadow', dragging && 'shadow-[0_0_0_3px_var(--brand)]')}
          onDragOver={(event) => {
            event.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={(event) => {
            event.preventDefault()
            setDragging(false)
            upload(event.dataTransfer.files[0])
          }}
        >
          <p className="text-sm font-medium">{t('Logo')}</p>
          <LogoPreview logoUrl={branding?.logoUrl} busy={busy} />
          <input
            ref={input}
            type="file"
            accept="image/png,image/jpeg,image/webp"
            className="hidden"
            aria-label={t('Choose a logo')}
            onChange={(event) => {
              upload(event.target.files?.[0])
              event.target.value = ''
            }}
          />
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={() => input.current?.click()} disabled={busy}>
              {branding?.logoUrl ? <Upload /> : <ImagePlus />}
              {branding?.logoUrl ? t('Replace logo') : t('Add logo')}
            </Button>
            {branding?.logoUrl && (
              <Button
                type="button"
                variant="ghost"
                disabled={busy}
                onClick={() =>
                  removeLogo.mutate(undefined, {
                    onSuccess: () => toast.success(t('Logo removed')),
                    onError: (error) => toast.error(errorMessage(error)),
                  })
                }
              >
                <Trash2 />
                {t('Remove')}
              </Button>
            )}
          </div>
          <p className="text-xs text-muted-foreground">
            {t(
              "PNG with a transparent background works best (JPEG and WebP are fine). Drop it here or choose a file; it's resized in your browser and saved straight away.",
            )}
          </p>
        </div>

        <Field data-invalid={fieldState.invalid} className="gap-3">
          <FieldLabel htmlFor={colourId}>{t('Colour')}</FieldLabel>
          <div className="flex items-center gap-2">
            <label className="relative size-10 shrink-0 cursor-pointer overflow-hidden rounded-lg border shadow-sm" title={t('Pick a colour')}>
              <span className="absolute inset-0" style={{ background: isColour(colour) ? colour : DEFAULT_BRAND_COLOUR }} />
              <input
                type="color"
                aria-label={t('Pick a colour')}
                className="absolute inset-0 cursor-pointer opacity-0"
                value={isColour(colour) ? colour.toLowerCase() : DEFAULT_BRAND_COLOUR}
                onChange={(event) => field.onChange(event.target.value)}
              />
            </label>
            <Input
              id={colourId}
              ref={fieldRef}
              value={colour}
              placeholder={t('{colour} (standard indigo)', { colour: DEFAULT_BRAND_COLOUR })}
              className="font-mono"
              maxLength={7}
              onChange={(event) => field.onChange(event.target.value.trim())}
              onBlur={field.onBlur}
              aria-invalid={fieldState.invalid}
            />
            <Button type="button" variant="ghost" size="icon" aria-label={t('Use the standard indigo')} title={t('Use the standard indigo')} disabled={colour === ''} onClick={() => field.onChange('')}>
              <RotateCcw />
            </Button>
          </div>
          <FieldError errors={[fieldState.error]} />

          <div className="flex flex-col gap-3 rounded-xl border p-3" aria-label={t('Preview')}>
            <div className="flex flex-wrap items-center gap-2">
              <span className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground">
                <Paintbrush className="size-4" /> {t('New order')}
              </span>
              <span className="inline-flex h-9 items-center rounded-lg bg-accent px-3 text-sm text-accent-foreground">{t('Selected row')}</span>
              <span className="inline-flex h-9 items-center rounded-lg px-3 text-sm font-medium text-white" style={{ background: 'var(--gradient-brand)' }}>
                {t('Highlight')}
              </span>
            </div>
            <div className="flex h-9 items-center gap-2 rounded-lg bg-sidebar px-3 text-sm text-sidebar-foreground">
              <span className="size-2 rounded-full bg-sidebar-primary" /> {t('Menu')}
            </div>
          </div>
          <FieldDescription>
            {theme?.adjusted
              ? t('Buttons use a slightly darker shade ({colour}) so white text on them stays easy to read; dark mode uses a lighter one.', { colour: theme.buttonHex })
              : t('Any colour works: the app picks lighter or darker shades wherever text needs them, in light and dark mode.')}{' '}
            {t('Everyone sees it once you save.')}
          </FieldDescription>
        </Field>
      </CardContent>
    </Card>
  )
}
