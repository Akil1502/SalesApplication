import { usePreferences } from '@/components/fx/preferences-context'
import { SegmentedControl } from '@/components/SegmentedControl'
import { LANGUAGES, t, type Language } from '@/lib/i18n'

/** English or தமிழ், each named in its own script so anyone can find theirs. */
export function LanguageSwitch({ className }: { className?: string }) {
  const preferences = usePreferences()
  return (
    <SegmentedControl<Language>
      label={t('Language')}
      value={preferences.language}
      onValueChange={preferences.setLanguage}
      options={LANGUAGES.map((l) => ({ value: l.value, label: <span lang={l.value}>{l.label}</span> }))}
      className={className}
    />
  )
}
