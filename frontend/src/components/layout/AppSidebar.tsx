import { ChartNoAxesCombined } from 'lucide-react'
import { motion } from 'motion/react'
import { NavLink, useLocation } from 'react-router'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from '@/components/ui/sidebar'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { findNavItem, navigation } from './navigation'
import { UserMenu } from './UserMenu'

export function AppSidebar() {
  const { can } = useAuth()
  const { pathname } = useLocation()
  const activePath = findNavItem(pathname)?.to

  const ai = useAiStatus()
  const aiEnabled = ai.data?.enabled ?? false

  const groups = navigation
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => can(item.permission) && (!item.ai || aiEnabled)),
    }))
    .filter((group) => group.items.length > 0)

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild>
              <NavLink to="/">
                <span
                  className="flex aspect-square size-8 items-center justify-center rounded-lg text-white shadow-[0_4px_14px_-2px_var(--brand-glow)]"
                  style={{ background: 'var(--gradient-brand)' }}
                >
                  <ChartNoAxesCombined className="size-4" />
                </span>
                <span className="grid flex-1 text-left leading-tight">
                  <span className="truncate font-semibold tracking-tight">SalesApp</span>
                  <span className="truncate text-xs text-sidebar-foreground/60">Sales &amp; GST billing</span>
                </span>
              </NavLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>

      <SidebarContent>
        {groups.map((group) => (
          <SidebarGroup key={group.label}>
            <SidebarGroupLabel className="text-sidebar-foreground/50">{group.label}</SidebarGroupLabel>
            <SidebarMenu>
              {group.items.map((item) => (
                <SidebarMenuItem key={item.to}>
                  <SidebarMenuButton asChild isActive={activePath === item.to} tooltip={item.title}>
                    <NavLink to={item.to} end className="relative isolate">
                      {/* The lit pill slides from the last page's item to this one. */}
                      {activePath === item.to && (
                        <motion.span
                          layoutId="sidebar-active"
                          aria-hidden
                          className="absolute inset-0 -z-10 rounded-md"
                          style={{
                            background: 'linear-gradient(90deg, oklch(0.6 0.16 268 / 38%), oklch(0.6 0.16 268 / 6%))',
                            boxShadow: 'inset 2px 0 0 var(--sidebar-primary)',
                          }}
                          transition={{ type: 'spring', stiffness: 420, damping: 34 }}
                        />
                      )}
                      <item.icon />
                      <span>{item.title}</span>
                    </NavLink>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroup>
        ))}
      </SidebarContent>

      <SidebarFooter>
        <UserMenu />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
