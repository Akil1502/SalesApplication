import { ChevronsUpDown, LogOut, Monitor, Moon, Sparkles, Sun } from 'lucide-react'
import { usePreferences } from '@/components/fx/preferences-context'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
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
import { SidebarMenu, SidebarMenuButton, SidebarMenuItem, useSidebar } from '@/components/ui/sidebar'
import { useAuth } from '@/features/auth/auth-context'
import type { MotionLevel, ThemeChoice } from '@/lib/preferences'

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

export function UserMenu() {
  const { user, logout } = useAuth()
  const { isMobile } = useSidebar()
  const preferences = usePreferences()
  const ThemeIcon = preferences.theme === 'system' ? Monitor : preferences.resolvedTheme === 'dark' ? Moon : Sun

  if (!user) return null

  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <SidebarMenuButton size="lg" className="data-[state=open]:bg-sidebar-accent">
              <Avatar className="size-8 rounded-lg">
                <AvatarFallback className="rounded-lg">{initials(user.fullName)}</AvatarFallback>
              </Avatar>
              <span className="grid flex-1 text-left text-sm leading-tight">
                <span className="truncate font-medium">{user.fullName}</span>
                <span className="truncate text-xs text-sidebar-foreground/60">{user.role}</span>
              </span>
              <ChevronsUpDown className="ml-auto size-4" />
            </SidebarMenuButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent
            className="min-w-56 rounded-lg"
            side={isMobile ? 'bottom' : 'right'}
            align="end"
            sideOffset={4}
          >
            <DropdownMenuLabel className="font-normal">
              <div className="grid text-sm leading-tight">
                <span className="truncate font-medium">{user.fullName}</span>
                <span className="truncate text-xs text-muted-foreground">{user.email}</span>
              </div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
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
                {!preferences.motionChosen && (
                  <p className="px-2 pt-1 pb-1.5 text-xs text-muted-foreground">Following your Windows animation setting.</p>
                )}
              </DropdownMenuSubContent>
            </DropdownMenuSub>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={logout}>
              <LogOut />
              Sign out
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </SidebarMenuItem>
    </SidebarMenu>
  )
}
