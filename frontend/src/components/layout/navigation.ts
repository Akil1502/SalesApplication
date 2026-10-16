import {
  BadgeIndianRupee,
  Banknote,
  Boxes,
  ClipboardList,
  Database,
  Factory,
  FileSpreadsheet,
  ChartColumn,
  FileText,
  History,
  LayoutDashboard,
  Lightbulb,
  MessageSquareText,
  Package,
  ReceiptIndianRupee,
  Settings,
  ShieldCheck,
  ShoppingCart,
  Sparkles,
  Tags,
  Truck,
  Undo2,
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
  /** One line for the mega menus and the search palette. */
  description: string
  /** The permission that shows the item and opens its page. */
  permission: string
  /** An AI feature: hidden while AI is switched off or has no API key. */
  ai?: boolean
}

export interface NavGroup {
  label: string
  /** The group's icon in the dock. */
  icon: LucideIcon
  items: NavItem[]
}

/** Single source of truth for the menus (rail, top bar and dock), the search palette and page titles. */
export const navigation: NavGroup[] = [
  {
    label: 'Overview',
    icon: LayoutDashboard,
    items: [
      { title: 'Dashboard', to: '/', icon: LayoutDashboard, description: 'Sales, receivables and stock at a glance', permission: Permissions.DashboardView },
    ],
  },
  {
    label: 'Sales',
    icon: ShoppingCart,
    items: [
      { title: 'Orders & quotations', to: '/orders', icon: ShoppingCart, description: 'Quote, confirm and invoice orders', permission: Permissions.OrdersView },
      {
        title: 'Smart order entry',
        to: '/orders/smart-entry',
        icon: Sparkles,
        description: 'Claude reads an order from text, a PDF or a photo',
        ai: true,
        permission: Permissions.SmartEntryUse,
      },
      { title: 'Invoices', to: '/invoices', icon: ReceiptIndianRupee, description: 'GST invoices, balances and printing', permission: Permissions.InvoicesView },
      { title: 'Payments', to: '/payments', icon: Wallet, description: 'Money received from customers', permission: Permissions.PaymentsView },
      { title: 'Credit notes', to: '/credit-notes', icon: Undo2, description: 'Returns and corrections after invoicing', permission: Permissions.CreditNotesView },
    ],
  },
  {
    label: 'Purchases',
    icon: Truck,
    items: [
      { title: 'Purchase orders', to: '/purchase-orders', icon: ClipboardList, description: 'Order from suppliers and receive goods', permission: Permissions.PurchaseOrdersView },
      { title: 'Supplier bills', to: '/supplier-bills', icon: FileSpreadsheet, description: 'Bills matched to orders and receipts', permission: Permissions.SupplierBillsView },
      { title: 'Supplier payments', to: '/supplier-payments', icon: Banknote, description: 'Money paid to suppliers', permission: Permissions.SupplierPaymentsView },
      { title: 'Supplier returns', to: '/supplier-returns', icon: Undo2, description: 'Goods sent back, with debit notes', permission: Permissions.SupplierReturnsView },
    ],
  },
  {
    label: 'Masters',
    icon: Database,
    items: [
      { title: 'Customers', to: '/customers', icon: Users, description: 'Buyers, GSTINs, credit limits and terms', permission: Permissions.CustomersView },
      { title: 'Suppliers', to: '/suppliers', icon: Factory, description: 'Who you buy from', permission: Permissions.SuppliersView },
      { title: 'Products', to: '/products', icon: Package, description: 'Catalogue, prices, photos and reorder levels', permission: Permissions.ProductsView },
      { title: 'Rate updates', to: '/rates', icon: BadgeIndianRupee, description: 'Rate lists for a period', permission: Permissions.RatesView },
      { title: 'Categories & HSN', to: '/catalog', icon: Tags, description: 'Product groups and GST rates', permission: Permissions.CatalogView },
    ],
  },
  {
    label: 'Inventory',
    icon: Boxes,
    items: [{ title: 'Stock', to: '/stock', icon: Boxes, description: 'On hand, the stock ledger and receipts', permission: Permissions.StockView }],
  },
  {
    label: 'Insights',
    icon: Lightbulb,
    items: [
      { title: 'Reports', to: '/reports', icon: ChartColumn, description: 'Sales, GST, ageing and stock reports', permission: Permissions.ReportsView },
      {
        title: 'Ask your data',
        to: '/ask',
        icon: MessageSquareText,
        description: 'Ask about your sales in plain English or Tamil',
        ai: true,
        permission: Permissions.AskDataUse,
      },
      { title: 'AI insights', to: '/insights', icon: Lightbulb, description: 'Forecasts, unusual changes and reorder advice', permission: Permissions.InsightsView },
    ],
  },
  {
    label: 'Administration',
    icon: ShieldCheck,
    items: [
      { title: 'Users & roles', to: '/admin/users', icon: UserCog, description: 'Accounts and what each role may do', permission: Permissions.UsersView },
      { title: 'Company settings', to: '/admin/settings', icon: Settings, description: 'Your GSTIN, address and bank details', permission: Permissions.SettingsEdit },
      { title: 'AI usage', to: '/admin/ai-usage', icon: FileText, description: 'What the AI features cost and did', permission: Permissions.AiUsageView },
      { title: 'Audit log', to: '/admin/audit', icon: History, description: 'Who changed what, and when', permission: Permissions.AuditView },
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

/** The menu groups and items this user may open (AI ones only while AI is on). */
export function allowedNavGroups(can: (...anyOf: string[]) => boolean, aiEnabled: boolean): NavGroup[] {
  return navigation
    .map((group) => ({ ...group, items: group.items.filter((item) => can(item.permission) && (!item.ai || aiEnabled)) }))
    .filter((group) => group.items.length > 0)
}

/** The menu items this user may open (AI ones only while AI is on). */
export function allowedNavItems(can: (...anyOf: string[]) => boolean, aiEnabled: boolean): NavItem[] {
  return allowedNavGroups(can, aiEnabled).flatMap((group) => group.items)
}
