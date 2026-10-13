import { useId, type ComponentProps, type ReactNode } from 'react'
import { useController, type Control, type FieldPath, type FieldValues } from 'react-hook-form'
import { Checkbox } from '@/components/ui/checkbox'
import { Field, FieldContent, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'

// Thin wrappers that bind react-hook-form fields to shadcn's Field layout, so every form shows labels,
// hints and errors (client- or server-side) the same way.

interface BaseFieldProps<T extends FieldValues> {
  control: Control<T>
  name: FieldPath<T>
  label: string
  description?: ReactNode
  disabled?: boolean
  className?: string
}

type InputProps = Omit<ComponentProps<'input'>, 'name' | 'value' | 'defaultValue' | 'onChange' | 'onBlur' | 'disabled'>

export function FormInput<T extends FieldValues>({
  control,
  name,
  label,
  description,
  disabled,
  className,
  ...inputProps
}: BaseFieldProps<T> & InputProps) {
  const id = useId()
  const {
    field: { ref: fieldRef, ...field },
    fieldState,
  } = useController({ control, name })
  return (
    <Field data-invalid={fieldState.invalid} className={className}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Input
        id={id}
        {...inputProps}
        name={field.name}
        ref={fieldRef}
        value={(field.value as string | number | null | undefined) ?? ''}
        onChange={field.onChange}
        onBlur={field.onBlur}
        disabled={disabled}
        aria-invalid={fieldState.invalid}
      />
      {description && !fieldState.error && <FieldDescription>{description}</FieldDescription>}
      <FieldError errors={[fieldState.error]} />
    </Field>
  )
}

export function FormTextarea<T extends FieldValues>({
  control,
  name,
  label,
  description,
  disabled,
  className,
  rows = 3,
}: BaseFieldProps<T> & { rows?: number }) {
  const id = useId()
  const {
    field: { ref: fieldRef, ...field },
    fieldState,
  } = useController({ control, name })
  return (
    <Field data-invalid={fieldState.invalid} className={className}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Textarea
        id={id}
        rows={rows}
        name={field.name}
        ref={fieldRef}
        value={(field.value as string | null | undefined) ?? ''}
        onChange={field.onChange}
        onBlur={field.onBlur}
        disabled={disabled}
        aria-invalid={fieldState.invalid}
      />
      {description && !fieldState.error && <FieldDescription>{description}</FieldDescription>}
      <FieldError errors={[fieldState.error]} />
    </Field>
  )
}

export interface SelectOption {
  value: string
  label: string
}

// Radix Select can't use '' as an item value, so "no selection" travels as this sentinel.
const NONE = '__none__'

export function FormSelect<T extends FieldValues>({
  control,
  name,
  label,
  description,
  disabled,
  className,
  options,
  placeholder = 'Select…',
  noneLabel,
}: BaseFieldProps<T> & {
  options: SelectOption[]
  placeholder?: string
  /** When set, adds an option that clears the value. */
  noneLabel?: string
}) {
  const id = useId()
  const {
    field: { ref: fieldRef, ...field },
    fieldState,
  } = useController({ control, name })
  const value = (field.value as string | null | undefined) || (noneLabel ? NONE : undefined)

  return (
    <Field data-invalid={fieldState.invalid} className={className}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Select
        value={value}
        onValueChange={(next) => field.onChange(next === NONE ? '' : next)}
        disabled={disabled}
        name={field.name}
      >
        <SelectTrigger id={id} ref={fieldRef} onBlur={field.onBlur} aria-invalid={fieldState.invalid} className="w-full">
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
        <SelectContent>
          {noneLabel && <SelectItem value={NONE}>{noneLabel}</SelectItem>}
          {options.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {description && !fieldState.error && <FieldDescription>{description}</FieldDescription>}
      <FieldError errors={[fieldState.error]} />
    </Field>
  )
}

export function FormSwitch<T extends FieldValues>({ control, name, label, description, disabled, className }: BaseFieldProps<T>) {
  const id = useId()
  const { field } = useController({ control, name })
  return (
    <Field orientation="horizontal" className={className}>
      <Switch id={id} checked={!!field.value} onCheckedChange={field.onChange} disabled={disabled} />
      <FieldContent>
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
        {description && <FieldDescription>{description}</FieldDescription>}
      </FieldContent>
    </Field>
  )
}

export function FormCheckbox<T extends FieldValues>({ control, name, label, description, disabled, className }: BaseFieldProps<T>) {
  const id = useId()
  const { field } = useController({ control, name })
  return (
    <Field orientation="horizontal" className={className}>
      <Checkbox id={id} checked={!!field.value} onCheckedChange={(checked) => field.onChange(checked === true)} disabled={disabled} />
      <FieldContent>
        <FieldLabel htmlFor={id} className="font-normal">
          {label}
        </FieldLabel>
        {description && <FieldDescription>{description}</FieldDescription>}
      </FieldContent>
    </Field>
  )
}
