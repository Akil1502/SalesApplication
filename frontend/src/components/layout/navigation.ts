import {
  BadgeIndianRupee,
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
import { Permissions } from '@/features/auth/permissions'

export interface NavItem {
  title: string
  to: string
  icon: LucideIcon
  /** The permission that shows the item and opens its page. */
  permission: string
  /** An AI feature: hidden while AI is switched off or has no API key. */
  ai?: boolean
}

export interface NavGroup {
  label: string
  items: NavItem[]
}

/** Single source of truth for the sidebar and page titles. */
export const navigation: NavGroup[] = [
  {
    label: 'Overview',
    items: [{ title: 'Dashboard', to: '/', icon: LayoutDashboard, permission: Permissions.DashboardView }],
  },
  {
    label: 'Sales',
    items: [
      { title: 'Orders & quotations', to: '/orders', icon: ShoppingCart, permission: Permissions.OrdersView },
      { title: 'Smart order entry', to: '/orders/smart-entry', icon: Sparkles, ai: true, permission: Permissions.SmartEntryUse },
      { title: 'Invoices', to: '/invoices', icon: ReceiptIndianRupee, permission: Permissions.InvoicesView },
      { title: 'Payments', to: '/payments', icon: Wallet, permission: Permissions.PaymentsView },
    ],
  },
  {
    label: 'Masters',
    items: [
      { title: 'Customers', to: '/customers', icon: Users, permission: Permissions.CustomersView },
      { title: 'Products', to: '/products', icon: Package, permission: Permissions.ProductsView },
      { title: 'Rate updates', to: '/rates', icon: BadgeIndianRupee, permission: Permissions.RatesView },
      { title: 'Categories & HSN', to: '/catalog', icon: Tags, permission: Permissions.CatalogView },
    ],
  },
  {
    label: 'Inventory',
    items: [{ title: 'Stock', to: '/stock', icon: Boxes, permission: Permissions.StockView }],
  },
  {
    label: 'Insights',
    items: [
      { title: 'Reports', to: '/reports', icon: ChartColumn, permission: Permissions.ReportsView },
      { title: 'Ask your data', to: '/ask', icon: MessageSquareText, ai: true, permission: Permissions.AskDataUse },
      { title: 'AI insights', to: '/insights', icon: Lightbulb, permission: Permissions.InsightsView },
    ],
  },
  {
    label: 'Administration',
    items: [
      { title: 'Users & roles', to: '/admin/users', icon: UserCog, permission: Permissions.UsersView },
      { title: 'Company settings', to: '/admin/settings', icon: Settings, permission: Permissions.SettingsEdit },
      { title: 'AI usage', to: '/admin/ai-usage', icon: FileText, permission: Permissions.AiUsageView },
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

/** The menu items this user may open (AI ones only while AI is on). */
export function allowedNavItems(can: (...anyOf: string[]) => boolean, aiEnabled: boolean): NavItem[] {
  return navigation.flatMap((group) => group.items).filter((item) => can(item.permission) && (!item.ai || aiEnabled))
}
