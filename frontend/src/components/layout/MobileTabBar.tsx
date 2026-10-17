import { ClipboardList, ClipboardPlus, HandCoins, House, Menu, Plus, ReceiptIndianRupee, ScanText, UserPlus, type LucideIcon } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type ReactNode } from 'react'
import { Link, NavLink, useLocation } from 'react-router'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { Permissions } from '@/features/auth/permissions'
import { RecordPaymentPicker } from '@/features/sales/RecordPaymentPicker'
import { cn } from '@/lib/utils'

interface Tab {
  to: string
  label: string
  icon: LucideIcon
  permission?: string
}

const LEFT: Tab[] = [
  { to: '/', label: 'Home', icon: House },
  { to: '/orders', label: 'Orders', icon: ClipboardList, permission: Permissions.OrdersView },
]
const RIGHT: Tab[] = [{ to: '/invoices', label: 'Invoices', icon: ReceiptIndianRupee, permission: Permissions.InvoicesView }]

const tabClass = 'relative flex h-full flex-1 flex-col items-center justify-center gap-0.5 text-[11px] font-medium'

function TabLink({ tab, active }: { tab: Tab; active: boolean }) {
  return (
    <NavLink to={tab.to} end={tab.to === '/'} className={cn(tabClass, active ? 'text-primary' : 'text-muted-foreground')}>
      {active && (
        <motion.span layoutId="tab-pill" aria-hidden className="absolute inset-x-3 top-1.5 bottom-1.5 -z-10 rounded-xl bg-accent" transition={{ type: 'spring', stiffness: 500, damping: 38 }} />
      )}
      <tab.icon className="size-5" aria-hidden />
      {tab.label}
    </NavLink>
  )
}

function ChoiceIcon({ icon: Icon, strong }: { icon: LucideIcon; strong?: boolean }) {
  return (
    <span
      className={cn('flex size-11 shrink-0 items-center justify-center rounded-xl', strong ? 'text-white' : 'bg-accent text-accent-foreground')}
      style={strong ? { background: 'var(--gradient-brand)' } : undefined}
    >
      <Icon className="size-5" aria-hidden />
    </span>
  )
}

function ChoiceText({ title, detail, strong }: { title: string; detail: ReactNode; strong?: boolean }) {
  return (
    <span>
      <span className={cn('block', strong ? 'font-semibold' : 'font-medium')}>{title}</span>
      <span className="block text-sm text-muted-foreground">{detail}</span>
    </span>
  )
}

/** The ＋ menu: what can be started from anywhere, for those allowed to. */
function QuickCreate({ open, onOpenChange, onPay }: { open: boolean; onOpenChange: (open: boolean) => void; onPay: () => void }) {
  const { can } = useAuth()
  const ai = useAiStatus()
  const choice = 'flex items-center gap-4 rounded-2xl border bg-card p-4 text-left transition-transform active:scale-[0.98]'
  const close = () => onOpenChange(false)

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom" className="gap-0 rounded-t-3xl pb-[max(1rem,env(safe-area-inset-bottom))]">
        <SheetHeader>
          <SheetTitle>Start something new</SheetTitle>
          <SheetDescription>Pick what you want to do.</SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-2 px-4">
          {can(Permissions.OrdersEdit) && (
            <Link to="/orders/new" onClick={close} className={cn(choice, 'border-primary/30')}>
              <ChoiceIcon icon={ClipboardPlus} strong />
              <ChoiceText title="New order" detail="A quotation or confirmed order for a customer" strong />
            </Link>
          )}
          {can(Permissions.SmartEntryUse) && ai.data?.enabled && (
            <Link to="/orders/smart-entry" onClick={close} className={choice}>
              <ChoiceIcon icon={ScanText} />
              <ChoiceText title="Smart order entry" detail="From a photo, PDF or WhatsApp message" />
            </Link>
          )}
          {can(Permissions.PaymentsEdit) && (
            <button
              type="button"
              className={choice}
              onClick={() => {
                close()
                onPay()
              }}
            >
              <ChoiceIcon icon={HandCoins} />
              <ChoiceText title="Record payment" detail="Money received against an invoice" />
            </button>
          )}
          {can(Permissions.CustomersEdit) && (
            <Link to="/customers/new" onClick={close} className={choice}>
              <ChoiceIcon icon={UserPlus} />
              <ChoiceText title="New customer" detail="With GSTIN, address and credit terms" />
            </Link>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}

/**
 * Phones: Home, Orders, a big ＋ and Invoices along the bottom, within reach of a thumb, and Menu for everything
 * else. Tabs someone may not use are left out; ＋ appears only when there's something they can start.
 */
export function MobileTabBar({ onMenu }: { onMenu: () => void }) {
  const { can } = useAuth()
  const { pathname } = useLocation()
  const [creating, setCreating] = useState(false)
  const [paying, setPaying] = useState(false)
  const allowed = (tab: Tab) => !tab.permission || can(tab.permission)
  const isActive = (tab: Tab) => (tab.to === '/' ? pathname === '/' : pathname === tab.to || pathname.startsWith(`${tab.to}/`))
  const canCreate = can(Permissions.OrdersEdit, Permissions.PaymentsEdit, Permissions.CustomersEdit, Permissions.SmartEntryUse)

  return (
    <>
      <nav
        aria-label="Quick menu"
        data-tour="nav"
        className="fx-glass fixed inset-x-0 bottom-0 z-40 flex h-[calc(4rem+env(safe-area-inset-bottom))] items-stretch border-t px-1 pb-[env(safe-area-inset-bottom)] md:hidden"
      >
        {LEFT.filter(allowed).map((tab) => (
          <TabLink key={tab.to} tab={tab} active={isActive(tab)} />
        ))}
        {canCreate && (
          <div className="flex flex-1 items-center justify-center">
            <motion.button
              type="button"
              data-tour="new"
              aria-label="Start something new"
              aria-expanded={creating}
              aria-haspopup="dialog"
              onClick={() => setCreating(true)}
              whileTap={{ scale: 0.92 }}
              className="-mt-6 flex size-14 items-center justify-center rounded-2xl text-white shadow-[0_10px_24px_-8px_var(--brand-glow)] ring-4 ring-background"
              style={{ background: 'var(--gradient-brand)' }}
            >
              <motion.span animate={{ rotate: creating ? 45 : 0 }} transition={{ type: 'spring', stiffness: 400, damping: 22 }}>
                <Plus className="size-6" />
              </motion.span>
            </motion.button>
          </div>
        )}
        {RIGHT.filter(allowed).map((tab) => (
          <TabLink key={tab.to} tab={tab} active={isActive(tab)} />
        ))}
        <button type="button" onClick={onMenu} className={cn(tabClass, 'text-muted-foreground')} aria-label="Open the full menu">
          <Menu className="size-5" aria-hidden />
          Menu
        </button>
      </nav>
      {canCreate && <QuickCreate open={creating} onOpenChange={setCreating} onPay={() => setPaying(true)} />}
      {can(Permissions.PaymentsEdit) && <RecordPaymentPicker open={paying} onOpenChange={setPaying} />}
    </>
  )
}
