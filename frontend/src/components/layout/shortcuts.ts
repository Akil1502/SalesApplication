import { Permissions } from '@/features/auth/permissions'

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
  { key: 'd', to: '/', title: 'Dashboard' },
  { key: 'o', to: '/orders', title: 'Orders & quotations' },
  { key: 'i', to: '/invoices', title: 'Invoices' },
  { key: 'c', to: '/customers', title: 'Customers' },
  { key: 'p', to: '/products', title: 'Products' },
  { key: 's', to: '/stock', title: 'Stock' },
  { key: 'b', to: '/supplier-bills', title: 'Supplier bills' },
  { key: 'r', to: '/reports', title: 'Reports' },
]

/** Everything the shortcut sheet lists, by group. */
export const SHORTCUT_GROUPS: { title: string; items: { keys: string[]; description: string }[] }[] = [
  {
    title: 'Anywhere',
    items: [
      { keys: ['Ctrl', 'K'], description: 'Search pages and records' },
      { keys: ['/'], description: "Search this page's list" },
      { keys: ['N'], description: 'New record on this list (orders, customers, products, suppliers, purchase orders)' },
      { keys: ['?'], description: 'Show these shortcuts' },
    ],
  },
  {
    title: 'Go to',
    items: GO_TO_PAGES.map((page) => ({ keys: ['G', page.key.toUpperCase()], description: page.title })),
  },
  {
    title: 'Lists',
    items: [
      { keys: ['J'], description: 'Next row' },
      { keys: ['K'], description: 'Previous row' },
      { keys: ['Enter'], description: 'Open the row' },
      { keys: ['X'], description: 'Tick the row (for export, printing or reminders)' },
    ],
  },
  {
    title: 'Side panel',
    items: [
      { keys: ['J'], description: 'Next record (or Alt+↓)' },
      { keys: ['K'], description: 'Previous record (or Alt+↑)' },
      { keys: ['Esc'], description: 'Close' },
    ],
  },
]
