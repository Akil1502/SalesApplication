import { Compass, Keyboard, Languages, LayoutPanelLeft, LogOut, Monitor, Moon, PanelBottom, PanelTop, Sparkles, Sun } from 'lucide-react'
import { usePreferences } from '@/components/fx/preferences-context'
import { useTour } from '@/components/tour/tour-context'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useAuth } from '@/features/auth/auth-context'
import { LANGUAGES, msg, t, type Language } from '@/lib/i18n'
import type { LayoutChoice, MotionLevel, ThemeChoice } from '@/lib/preferences'

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts.at(-1)?.[0] ?? '') : '')).toUpperCase() || '?'
}

const themes: { value: ThemeChoice; label: string }[] = [
  { value: 'light', label: msg('Light') },
  { value: 'dark', label: msg('Dark') },
  { value: 'system', label: msg('Same as Windows') },
]

const motionLevels: { value: MotionLevel; label: string; hint: string }[] = [
  { value: 'full', label: msg('Full'), hint: msg('3D scenes and depth') },
  { value: 'subtle', label: msg('Subtle'), hint: msg('Quick fades only') },
  { value: 'off', label: msg('Off'), hint: msg('No animation') },
]

const layouts: { value: LayoutChoice; label: string; hint: string; icon: typeof PanelTop }[] = [
  { value: 'rail', label: msg('Icon rail'), hint: msg('Icons on the left that open on hover'), icon: LayoutPanelLeft },
  { value: 'top', label: msg('Top menus'), hint: msg('Large menus across the top'), icon: PanelTop },
  { value: 'dock', label: msg('Dock'), hint: msg('A dock at the bottom of the screen'), icon: PanelBottom },
]

/** The signed-in user's avatar in the top bar: appearance, motion, layout, language, and signing out. */
export function UserMenu({ onShortcuts }: { onShortcuts: () => void }) {
  const { user, logout } = useAuth()
  const preferences = usePreferences()
  const tour = useTour()
  const ThemeIcon = preferences.theme === 'system' ? Monitor : preferences.resolvedTheme === 'dark' ? Moon : Sun
  const LayoutIcon = layouts.find((l) => l.value === preferences.layout)?.icon ?? LayoutPanelLeft

  if (!user) return null

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="rounded-full" aria-label={t('Account menu for {name}', { name: user.fullName })} data-tour="account">
          <Avatar className="size-8">
            <AvatarFallback className="text-xs font-semibold text-white" style={{ background: 'var(--gradient-brand)' }}>
              {initials(user.fullName)}
            </AvatarFallback>
          </Avatar>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="min-w-60 rounded-xl" align="end" sideOffset={8}>
        <DropdownMenuLabel className="font-normal">
          <div className="grid text-sm leading-tight">
            <span className="truncate font-medium">{user.fullName}</span>
            <span className="truncate text-xs text-muted-foreground">{user.email}</span>
            <span className="truncate text-xs text-muted-foreground">{t(user.role)}</span>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <LayoutIcon />
            {t('Layout')}
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent className="min-w-64">
            <DropdownMenuRadioGroup value={preferences.layout} onValueChange={(value) => preferences.setLayout(value as LayoutChoice)}>
              {layouts.map((layout) => (
                <DropdownMenuRadioItem key={layout.value} value={layout.value}>
                  <span className="grid leading-tight">
                    <span>{t(layout.label)}</span>
                    <span className="text-xs text-muted-foreground">{t(layout.hint)}</span>
                  </span>
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <ThemeIcon />
            {t('Appearance')}
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={preferences.theme} onValueChange={(value) => preferences.setTheme(value as ThemeChoice)}>
              {themes.map((theme) => (
                <DropdownMenuRadioItem key={theme.value} value={theme.value}>
                  {t(theme.label)}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <Sparkles />
            {t('Motion')}
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent className="min-w-52">
            <DropdownMenuRadioGroup value={preferences.motion} onValueChange={(value) => preferences.setMotion(value as MotionLevel)}>
              {motionLevels.map((level) => (
                <DropdownMenuRadioItem key={level.value} value={level.value}>
                  <span className="grid leading-tight">
                    <span>{t(level.label)}</span>
                    <span className="text-xs text-muted-foreground">{t(level.hint)}</span>
                  </span>
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
            {!preferences.motionChosen && <p className="px-2 pt-1 pb-1.5 text-xs text-muted-foreground">{t('Following your Windows animation setting.')}</p>}
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <Languages />
            {t('Language')}
            {/* Named in Tamil too, so someone who reads only Tamil can find it. */}
            {preferences.language === 'en' && (
              <span lang="ta" className="ml-auto pl-2 text-xs text-muted-foreground">
                மொழி
              </span>
            )}
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={preferences.language} onValueChange={(value) => preferences.setLanguage(value as Language)}>
              {LANGUAGES.map((language) => (
                <DropdownMenuRadioItem key={language.value} value={language.value} lang={language.value}>
                  {language.label}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuItem onSelect={onShortcuts} className="hidden md:flex">
          <Keyboard />
          {t('Keyboard shortcuts')}
          <span className="ml-auto text-xs text-muted-foreground">?</span>
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={tour.start}>
          <Compass />
          {t('Take the tour')}
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={logout}>
          <LogOut />
          {t('Sign out')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
