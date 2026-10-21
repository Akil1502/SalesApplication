import { Permissions } from '@/features/auth/permissions'
import { msg, t } from '@/lib/i18n'
import type { LayoutChoice } from '@/lib/preferences'

/** Bump when the tour changes enough that everyone should see it again. */
export const TOUR_VERSION = 1

export interface TourStep {
  id: string
  /** The data-tour value of the element to point at; none: a card in the middle of the screen. */
  target?: string
  title: string
  body: string
}

export interface TourContext {
  /** What the app calls itself (the company, once it has a logo). */
  appName: string
  firstName: string
  layout: LayoutChoice
  phone: boolean
  can: (...anyOf: string[]) => boolean
  aiEnabled: boolean
}

const MENU: Record<LayoutChoice, string> = {
  rail: msg('Every page is in the menu on the left. Point at it and it opens with names; the page moves over to make room.'),
  top: msg('Every page is in the menus along the top. Point at one to see its pages, each with a line about what it’s for.'),
  dock: msg('Every page is in the dock at the bottom. Its icons grow as you point at them and pop up their pages.'),
}

/** The steps for this person: what they can do, on this screen, in their chosen layout, in the language on screen. */
export function tourSteps({ appName, firstName, layout, phone, can, aiEnabled }: TourContext): TourStep[] {
  const steps: (TourStep | false)[] = [
    {
      id: 'welcome',
      title: firstName ? t('Welcome to {app}, {name}', { app: appName, name: firstName }) : t('Welcome to {app}', { app: appName }),
      body: t('Here’s a one-minute look around. Leave whenever you like; you can take it again from your account menu.'),
    },
    {
      id: 'menu',
      target: 'nav',
      title: t('Finding your way'),
      body: phone
        ? t('Home, orders and invoices are one tap away at the bottom. Menu has every other page.')
        : t('{menu} You can switch to another layout from your account menu.', { menu: t(MENU[layout]) }),
    },
    phone &&
      can(Permissions.OrdersEdit, Permissions.PaymentsEdit, Permissions.CustomersEdit) && {
        id: 'new',
        target: 'new',
        title: t('Start something from anywhere'),
        body: t('The ＋ button starts a new order, records a payment or adds a customer, whichever page you’re on.'),
      },
    {
      id: 'search',
      target: 'search',
      title: t('Find anything'),
      body: phone
        ? t('Tap the magnifier to find a customer, invoice, order or product by name or number, or jump to a page.')
        : t('Press Ctrl+K (⌘K on a Mac) to find a customer, invoice, order or product by name or number, jump to a page or start something new.'),
    },
    {
      id: 'notifications',
      target: 'notifications',
      title: t('Notifications'),
      body: t('The bell tells you about payments received, new orders, overdue invoices and anything waiting for you, only for what your role lets you see. The gear inside chooses which kinds you get.'),
    },
    can(Permissions.DashboardView) && {
      id: 'dashboard',
      target: 'dashboard-controls',
      title: t('Your dashboard'),
      body: t('Choose the period the figures cover. Customise lets you drag, resize, hide and add cards, such as invoices to chase or quick actions. Only you see your layout.'),
    },
    !phone && {
      id: 'records',
      title: t('Records open beside their list'),
      body: t('Click an invoice, order or customer and it slides in beside the list, which stays where you left it. J and K step to the next and previous one; Esc closes it.'),
    },
    can(Permissions.SmartEntryUse) &&
      aiEnabled && {
        id: 'smart-entry',
        title: t('Let Claude read the order'),
        body: t('Smart order entry turns a WhatsApp message, PDF or photo into a draft order for you to check. Nothing is saved until you confirm.'),
      },
    can(Permissions.PurchaseOrdersApprove, Permissions.SupplierBillsApprove) && {
      id: 'approvals',
      title: t('Waiting for you'),
      body: t('Purchase orders above the approval limit, and supplier bills that don’t match their order, wait for someone who can approve them. The Purchases card on the dashboard counts them.'),
    },
    {
      id: 'account',
      target: 'account',
      title: t('Make it yours'),
      body: t('Your account menu has the layout, light or dark, how much motion you like, and this tour if you want it again.'),
    },
    {
      id: 'done',
      title: t('That’s it'),
      body: phone ? t('You’re ready to go.') : t('Press ? any time for the keyboard shortcuts. You’re ready to go.'),
    },
  ]
  return steps.filter((step): step is TourStep => step !== false)
}
