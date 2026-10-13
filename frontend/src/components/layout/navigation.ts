import {
  Boxes,
  ChartColumn,
  FileText,
  LayoutDashboard,
  Lightbulb,
  MessageSquareText,
  Package,
  ReceiptIndianRupee,
  Settings,
  ShoppingCart,
  Sparkles,
  Tags,
  Users,
  UserCog,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import { Roles } from '@/features/auth/auth-context'

export interface NavItem {
  title: string
  to: string
  icon: LucideIcon
  /** Only these roles see the item; omitted means everyone. */
  roles?: string[]
  /** Build phase that delivers the screen; shown as a "coming" placeholder until then. */
  phase?: string
}

export interface NavGroup {
  label: string
  items: NavItem[]
}

const managers = [Roles.Admin, Roles.SalesManager]
const admins = [Roles.Admin]

/** Single source of truth for the sidebar, page titles and placeholder routes. */
export const navigation: NavGroup[] = [
  {
    label: 'Overview',
    items: [{ title: 'Dashboard', to: '/', icon: LayoutDashboard }],
  },
  {
    label: 'Sales',
    items: [
      { title: 'Orders & quotations', to: '/orders', icon: ShoppingCart },
      { title: 'Smart order entry', to: '/orders/smart-entry', icon: Sparkles, phase: 'P5' },
      { title: 'Invoices', to: '/invoices', icon: ReceiptIndianRupee },
      { title: 'Payments', to: '/payments', icon: Wallet },
    ],
  },
  {
    label: 'Masters',
    items: [
      { title: 'Customers', to: '/customers', icon: Users },
      { title: 'Products', to: '/products', icon: Package },
      { title: 'Categories & HSN', to: '/catalog', icon: Tags, roles: managers },
    ],
  },
  {
    label: 'Inventory',
    items: [{ title: 'Stock', to: '/stock', icon: Boxes }],
  },
  {
    label: 'Insights',
    items: [
      { title: 'Reports', to: '/reports', icon: ChartColumn, phase: 'P4' },
      { title: 'Ask your data', to: '/ask', icon: MessageSquareText, phase: 'P6' },
      { title: 'AI insights', to: '/insights', icon: Lightbulb, roles: managers, phase: 'P8' },
    ],
  },
  {
    label: 'Administration',
    items: [
      { title: 'Users', to: '/admin/users', icon: UserCog, roles: admins },
      { title: 'Company settings', to: '/admin/settings', icon: Settings, roles: admins },
      { title: 'AI usage', to: '/admin/ai-usage', icon: FileText, roles: admins, phase: 'P9' },
    ],
  },
]

/** The nav item whose path best matches the current location, e.g. /customers/42 -> Customers. */
export function findNavItem(pathname: string): NavItem | undefined {
  const items = navigation.flatMap((group) => group.items)
  return (
    items.find((item) => item.to === pathname) ??
    items
      .filter((item) => item.to !== '/' && pathname.startsWith(`${item.to}/`))
      .sort((a, b) => b.to.length - a.to.length)[0]
  )
}
