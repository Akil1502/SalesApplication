import { Compass, Keyboard, LayoutPanelLeft, LogOut, Monitor, Moon, PanelBottom, PanelTop, Sparkles, Sun } from 'lucide-react'
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
import type { LayoutChoice, MotionLevel, ThemeChoice } from '@/lib/preferences'

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts.at(-1)?.[0] ?? '') : '')).toUpperCase() || '?'
}

const themes: { value: ThemeChoice; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'Same as Windows' },
]

const motionLevels: { value: MotionLevel; label: string; hint: string }[] = [
  { value: 'full', label: 'Full', hint: '3D scenes and depth' },
  { value: 'subtle', label: 'Subtle', hint: 'Quick fades only' },
  { value: 'off', label: 'Off', hint: 'No animation' },
]

const layouts: { value: LayoutChoice; label: string; hint: string; icon: typeof PanelTop }[] = [
  { value: 'rail', label: 'Icon rail', hint: 'Icons on the left that open on hover', icon: LayoutPanelLeft },
  { value: 'top', label: 'Top menus', hint: 'Large menus across the top', icon: PanelTop },
  { value: 'dock', label: 'Dock', hint: 'A dock at the bottom of the screen', icon: PanelBottom },
]

/** The signed-in user's avatar in the top bar: appearance, motion, layout, and signing out. */
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
        <Button variant="ghost" size="icon" className="rounded-full" aria-label={`Account menu for ${user.fullName}`} data-tour="account">
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
            <span className="truncate text-xs text-muted-foreground">{user.role}</span>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <LayoutIcon />
            Layout
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent className="min-w-64">
            <DropdownMenuRadioGroup value={preferences.layout} onValueChange={(value) => preferences.setLayout(value as LayoutChoice)}>
              {layouts.map((layout) => (
                <DropdownMenuRadioItem key={layout.value} value={layout.value}>
                  <span className="grid leading-tight">
                    <span>{layout.label}</span>
                    <span className="text-xs text-muted-foreground">{layout.hint}</span>
                  </span>
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <ThemeIcon />
            Appearance
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={preferences.theme} onValueChange={(value) => preferences.setTheme(value as ThemeChoice)}>
              {themes.map((theme) => (
                <DropdownMenuRadioItem key={theme.value} value={theme.value}>
                  {theme.label}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>
            <Sparkles />
            Motion
          </DropdownMenuSubTrigger>
          <DropdownMenuSubContent className="min-w-52">
            <DropdownMenuRadioGroup value={preferences.motion} onValueChange={(value) => preferences.setMotion(value as MotionLevel)}>
              {motionLevels.map((level) => (
                <DropdownMenuRadioItem key={level.value} value={level.value}>
                  <span className="grid leading-tight">
                    <span>{level.label}</span>
                    <span className="text-xs text-muted-foreground">{level.hint}</span>
                  </span>
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
            {!preferences.motionChosen && <p className="px-2 pt-1 pb-1.5 text-xs text-muted-foreground">Following your Windows animation setting.</p>}
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuItem onSelect={onShortcuts} className="hidden md:flex">
          <Keyboard />
          Keyboard shortcuts
          <span className="ml-auto text-xs text-muted-foreground">?</span>
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={tour.start}>
          <Compass />
          Take the tour
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={logout}>
          <LogOut />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
