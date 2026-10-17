import { Permissions } from '@/features/auth/permissions'
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
  rail: 'Every page is in the menu on the left. Point at it and it opens with names; the page moves over to make room.',
  top: 'Every page is in the menus along the top. Point at one to see its pages, each with a line about what it’s for.',
  dock: 'Every page is in the dock at the bottom. Its icons grow as you point at them and pop up their pages.',
}

/** The steps for this person: what they can do, on this screen, in their chosen layout. */
export function tourSteps({ appName, firstName, layout, phone, can, aiEnabled }: TourContext): TourStep[] {
  const steps: (TourStep | false)[] = [
    {
      id: 'welcome',
      title: firstName ? `Welcome to ${appName}, ${firstName}` : `Welcome to ${appName}`,
      body: 'Here’s a one-minute look around. Leave whenever you like; you can take it again from your account menu.',
    },
    {
      id: 'menu',
      target: 'nav',
      title: 'Finding your way',
      body: phone
        ? 'Home, orders and invoices are one tap away at the bottom. Menu has every other page.'
        : `${MENU[layout]} You can switch to another layout from your account menu.`,
    },
    phone &&
      can(Permissions.OrdersEdit, Permissions.PaymentsEdit, Permissions.CustomersEdit) && {
        id: 'new',
        target: 'new',
        title: 'Start something from anywhere',
        body: 'The ＋ button starts a new order, records a payment or adds a customer, whichever page you’re on.',
      },
    {
      id: 'search',
      target: 'search',
      title: 'Find anything',
      body: phone
        ? 'Tap the magnifier to find a customer, invoice, order or product by name or number, or jump to a page.'
        : 'Press Ctrl+K (⌘K on a Mac) to find a customer, invoice, order or product by name or number, jump to a page or start something new.',
    },
    can(Permissions.DashboardView) && {
      id: 'dashboard',
      target: 'dashboard-controls',
      title: 'Your dashboard',
      body: 'Choose the period the figures cover. Customise lets you drag, resize, hide and add cards, such as invoices to chase or quick actions. Only you see your layout.',
    },
    !phone && {
      id: 'records',
      title: 'Records open beside their list',
      body: 'Click an invoice, order or customer and it slides in beside the list, which stays where you left it. J and K step to the next and previous one; Esc closes it.',
    },
    can(Permissions.SmartEntryUse) &&
      aiEnabled && {
        id: 'smart-entry',
        title: 'Let Claude read the order',
        body: 'Smart order entry turns a WhatsApp message, PDF or photo into a draft order for you to check. Nothing is saved until you confirm.',
      },
    can(Permissions.PurchaseOrdersApprove, Permissions.SupplierBillsApprove) && {
      id: 'approvals',
      title: 'Waiting for you',
      body: 'Purchase orders above the approval limit, and supplier bills that don’t match their order, wait for someone who can approve them. The Purchases card on the dashboard counts them.',
    },
    {
      id: 'account',
      target: 'account',
      title: 'Make it yours',
      body: 'Your account menu has the layout, light or dark, how much motion you like, and this tour if you want it again.',
    },
    {
      id: 'done',
      title: 'That’s it',
      body: phone ? 'You’re ready to go.' : 'Press ? any time for the keyboard shortcuts. You’re ready to go.',
    },
  ]
  return steps.filter((step): step is TourStep => step !== false)
}
