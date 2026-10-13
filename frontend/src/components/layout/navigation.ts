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

/** Single source of truth for the sidebar and the placeholder routes. */
export const navigation: NavGroup[] = [
  {
    label: 'Overview',
    items: [{ title: 'Dashboard', to: '/', icon: LayoutDashboard }],
  },
  {
    label: 'Sales',
    items: [
      { title: 'Orders & quotations', to: '/orders', icon: ShoppingCart, phase: 'P3' },
      { title: 'Smart order entry', to: '/orders/smart-entry', icon: Sparkles, phase: 'P5' },
      { title: 'Invoices', to: '/invoices', icon: ReceiptIndianRupee, phase: 'P3' },
      { title: 'Payments', to: '/payments', icon: Wallet, phase: 'P3' },
    ],
  },
  {
    label: 'Masters',
    items: [
      { title: 'Customers', to: '/customers', icon: Users, phase: 'P1' },
      { title: 'Products', to: '/products', icon: Package, phase: 'P1' },
    ],
  },
  {
    label: 'Inventory',
    items: [{ title: 'Stock', to: '/stock', icon: Boxes, phase: 'P2' }],
  },
  {
    label: 'Insights',
    items: [
      { title: 'Reports', to: '/reports', icon: ChartColumn, phase: 'P4' },
      { title: 'Ask your data', to: '/ask', icon: MessageSquareText, phase: 'P6' },
      {
        title: 'AI insights',
        to: '/insights',
        icon: Lightbulb,
        roles: [Roles.Admin, Roles.SalesManager],
        phase: 'P8',
      },
    ],
  },
  {
    label: 'Administration',
    items: [
      { title: 'Users', to: '/admin/users', icon: UserCog, roles: [Roles.Admin], phase: 'P1' },
      { title: 'Company settings', to: '/admin/settings', icon: Settings, roles: [Roles.Admin], phase: 'P1' },
      { title: 'AI usage', to: '/admin/ai-usage', icon: FileText, roles: [Roles.Admin], phase: 'P9' },
    ],
  },
]
