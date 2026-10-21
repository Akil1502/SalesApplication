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
import { msg } from '@/lib/i18n'

/** Titles, descriptions and group labels are English marked with msg(); useNav() hands them out translated. */
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
    label: msg('Overview'),
    icon: LayoutDashboard,
    items: [
      { title: msg('Dashboard'), to: '/', icon: LayoutDashboard, description: msg('Sales, receivables and stock at a glance'), permission: Permissions.DashboardView },
    ],
  },
  {
    label: msg('Sales'),
    icon: ShoppingCart,
    items: [
      { title: msg('Orders & quotations'), to: '/orders', icon: ShoppingCart, description: msg('Quote, confirm and invoice orders'), permission: Permissions.OrdersView },
      {
        title: msg('Smart order entry'),
        to: '/orders/smart-entry',
        icon: Sparkles,
        description: msg('Claude reads an order from text, a PDF or a photo'),
        ai: true,
        permission: Permissions.SmartEntryUse,
      },
      { title: msg('Invoices'), to: '/invoices', icon: ReceiptIndianRupee, description: msg('GST invoices, balances and printing'), permission: Permissions.InvoicesView },
      { title: msg('Payments'), to: '/payments', icon: Wallet, description: msg('Money received from customers'), permission: Permissions.PaymentsView },
      { title: msg('Credit notes'), to: '/credit-notes', icon: Undo2, description: msg('Returns and corrections after invoicing'), permission: Permissions.CreditNotesView },
    ],
  },
  {
    label: msg('Purchases'),
    icon: Truck,
    items: [
      { title: msg('Purchase orders'), to: '/purchase-orders', icon: ClipboardList, description: msg('Order from suppliers and receive goods'), permission: Permissions.PurchaseOrdersView },
      { title: msg('Supplier bills'), to: '/supplier-bills', icon: FileSpreadsheet, description: msg('Bills matched to orders and receipts'), permission: Permissions.SupplierBillsView },
      { title: msg('Supplier payments'), to: '/supplier-payments', icon: Banknote, description: msg('Money paid to suppliers'), permission: Permissions.SupplierPaymentsView },
      { title: msg('Supplier returns'), to: '/supplier-returns', icon: Undo2, description: msg('Goods sent back, with debit notes'), permission: Permissions.SupplierReturnsView },
    ],
  },
  {
    label: msg('Masters'),
    icon: Database,
    items: [
      { title: msg('Customers'), to: '/customers', icon: Users, description: msg('Buyers, GSTINs, credit limits and terms'), permission: Permissions.CustomersView },
      { title: msg('Suppliers'), to: '/suppliers', icon: Factory, description: msg('Who you buy from'), permission: Permissions.SuppliersView },
      { title: msg('Products'), to: '/products', icon: Package, description: msg('Catalogue, prices, photos and reorder levels'), permission: Permissions.ProductsView },
      { title: msg('Rate updates'), to: '/rates', icon: BadgeIndianRupee, description: msg('Rate lists for a period'), permission: Permissions.RatesView },
      { title: msg('Categories & HSN'), to: '/catalog', icon: Tags, description: msg('Product groups and GST rates'), permission: Permissions.CatalogView },
    ],
  },
  {
    label: msg('Inventory'),
    icon: Boxes,
    items: [{ title: msg('Stock'), to: '/stock', icon: Boxes, description: msg('On hand, the stock ledger and receipts'), permission: Permissions.StockView }],
  },
  {
    label: msg('Insights'),
    icon: Lightbulb,
    items: [
      { title: msg('Reports'), to: '/reports', icon: ChartColumn, description: msg('Sales, GST, ageing and stock reports'), permission: Permissions.ReportsView },
      {
        title: msg('Ask your data'),
        to: '/ask',
        icon: MessageSquareText,
        description: msg('Ask about your sales in plain English or Tamil'),
        ai: true,
        permission: Permissions.AskDataUse,
      },
      { title: msg('AI insights'), to: '/insights', icon: Lightbulb, description: msg('Forecasts, unusual changes and reorder advice'), permission: Permissions.InsightsView },
    ],
  },
  {
    label: msg('Administration'),
    icon: ShieldCheck,
    items: [
      { title: msg('Users & roles'), to: '/admin/users', icon: UserCog, description: msg('Accounts and what each role may do'), permission: Permissions.UsersView },
      { title: msg('Company settings'), to: '/admin/settings', icon: Settings, description: msg('Your GSTIN, address and bank details'), permission: Permissions.SettingsEdit },
      { title: msg('AI usage'), to: '/admin/ai-usage', icon: FileText, description: msg('What the AI features cost and did'), permission: Permissions.AiUsageView },
      { title: msg('Audit log'), to: '/admin/audit', icon: History, description: msg('Who changed what, and when'), permission: Permissions.AuditView },
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
