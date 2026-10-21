import { Permissions } from '@/features/auth/permissions'
import { msg } from '@/lib/i18n'

/** N on a list page starts a new record there, for users who may create one. */
export const NEW_RECORD_PAGES = [
  { list: '/orders', to: '/orders/new', permission: Permissions.OrdersEdit },
  { list: '/customers', to: '/customers/new', permission: Permissions.CustomersEdit },
  { list: '/products', to: '/products/new', permission: Permissions.ProductsEdit },
  { list: '/suppliers', to: '/suppliers/new', permission: Permissions.SuppliersEdit },
  { list: '/purchase-orders', to: '/purchase-orders/new', permission: Permissions.PurchaseOrdersEdit },
] as const

/** G then a letter jumps to a page. */
export const GO_TO_PAGES: { key: string; to: string; title: string }[] = [
  { key: 'd', to: '/', title: msg('Dashboard') },
  { key: 'o', to: '/orders', title: msg('Orders & quotations') },
  { key: 'i', to: '/invoices', title: msg('Invoices') },
  { key: 'c', to: '/customers', title: msg('Customers') },
  { key: 'p', to: '/products', title: msg('Products') },
  { key: 's', to: '/stock', title: msg('Stock') },
  { key: 'b', to: '/supplier-bills', title: msg('Supplier bills') },
  { key: 'r', to: '/reports', title: msg('Reports') },
]

/** Everything the shortcut sheet lists, by group (English marked with msg(), translated where shown). */
export const SHORTCUT_GROUPS: { title: string; items: { keys: string[]; description: string }[] }[] = [
  {
    title: msg('Anywhere'),
    items: [
      { keys: ['Ctrl', 'K'], description: msg('Search pages and records') },
      { keys: ['/'], description: msg("Search this page's list") },
      { keys: ['N'], description: msg('New record on this list (orders, customers, products, suppliers, purchase orders)') },
      { keys: ['?'], description: msg('Show these shortcuts') },
    ],
  },
  {
    title: msg('Go to'),
    items: GO_TO_PAGES.map((page) => ({ keys: ['G', page.key.toUpperCase()], description: page.title })),
  },
  {
    title: msg('Lists'),
    items: [
      { keys: ['J'], description: msg('Next row') },
      { keys: ['K'], description: msg('Previous row') },
      { keys: ['Enter'], description: msg('Open the row') },
      { keys: ['X'], description: msg('Tick the row (for export, printing or reminders)') },
    ],
  },
  {
    title: msg('Side panel'),
    items: [
      { keys: ['J'], description: msg('Next record (or Alt+↓)') },
      { keys: ['K'], description: msg('Previous record (or Alt+↑)') },
      { keys: ['Esc'], description: msg('Close') },
    ],
  },
]
