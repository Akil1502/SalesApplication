import { BellRing, CheckCheck, ClipboardPlus, FileWarning, HandCoins, ScanText, ShoppingBag, Timer, UserPlus } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useAiStatus } from '@/features/ai/api'
import { useAuth } from '@/features/auth/auth-context'
import { useMarkAllRead, useMarkRead, useNotifications } from '@/features/notifications/api'
import { NotificationList } from '@/features/notifications/NotificationList'
import { Permissions } from '@/features/auth/permissions'
import { usePurchaseOrders, useSupplierBills } from '@/features/purchasing/api'
import { useInvoices, usePayments, type InvoiceListItem } from '@/features/sales/api'
import { paymentModeLabel } from '@/features/sales/labels'
import { RecordPaymentPicker } from '@/features/sales/RecordPaymentPicker'
import { ReminderDialog } from '@/features/sales/ReminderDialog'
import { formatBusinessDate, formatINR, formatINRCompact } from '@/lib/format'
import { cn } from '@/lib/utils'

function Rows({ loading, empty, children }: { loading: boolean; empty: string | false; children: ReactNode }) {
  if (loading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-9 w-full" />
        ))}
      </div>
    )
  }
  return empty ? <p className="py-6 text-center text-sm text-muted-foreground">{empty}</p> : <ul className="-my-1 flex flex-col divide-y">{children}</ul>
}

/** The most overdue invoices, oldest due date first, with a payment reminder for one customer or all of them. */
export function ChaseCard() {
  const invoices = useInvoices({ overdueOnly: true, sort: 'dueDate', page: 1, pageSize: 5 })
  const [reminding, setReminding] = useState<InvoiceListItem[] | null>(null)
  const items = invoices.data?.items ?? []

  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Invoices to chase</CardTitle>
        <CardDescription>{invoices.data ? `${invoices.data.totalCount.toLocaleString('en-IN')} overdue · most overdue first` : 'Most overdue first'}</CardDescription>
        <CardAction>
          <Button variant="ghost" size="sm" disabled={items.length === 0} onClick={() => setReminding(items)}>
            <BellRing /> Remind all
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        <Rows loading={invoices.isPending} empty={items.length === 0 && 'Nothing is overdue.'}>
          {items.map((invoice) => (
            <li key={invoice.id} className="flex items-center gap-3 py-2 text-sm">
              <Link to={`/invoices/${invoice.id}`} className="min-w-0 flex-1 hover:underline">
                <span className="block truncate font-medium">{invoice.customerName}</span>
                <span className="block text-xs text-muted-foreground">{invoice.number}</span>
              </Link>
              <span className="text-right">
                <span className="block font-medium tabular-nums">{formatINR(invoice.balance)}</span>
                <span className={cn('block text-xs', (invoice.daysOverdue ?? 0) > 60 ? 'text-destructive' : 'text-muted-foreground')}>
                  {invoice.daysOverdue} {invoice.daysOverdue === 1 ? 'day' : 'days'} late
                </span>
              </span>
              <Button variant="ghost" size="icon" className="size-8" aria-label={`Payment reminder to ${invoice.customerName}`} title="Payment reminder" onClick={() => setReminding([invoice])}>
                <BellRing />
              </Button>
            </li>
          ))}
        </Rows>
        {items.length > 0 && (
          <Button asChild variant="link" size="sm" className="mt-2 px-0">
            <Link to="/invoices?view=overdue">All overdue invoices</Link>
          </Button>
        )}
      </CardContent>
      <ReminderDialog invoices={reminding} onOpenChange={(open) => !open && setReminding(null)} />
    </Card>
  )
}

/** The latest money received. */
export function RecentPaymentsCard() {
  const payments = usePayments({ page: 1, pageSize: 6 })
  const items = payments.data?.items ?? []
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Recent payments</CardTitle>
        <CardDescription>The latest money received from customers</CardDescription>
        <CardAction>
          <Button asChild variant="ghost" size="sm">
            <Link to="/payments">All</Link>
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        <Rows loading={payments.isPending} empty={items.length === 0 && 'No payments yet.'}>
          {items.map((payment) => (
            <li key={payment.id} className="flex items-center gap-3 py-2 text-sm">
              <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-accent text-accent-foreground">
                <HandCoins className="size-4" aria-hidden />
              </span>
              <Link to={`/invoices/${payment.invoiceId}`} className="min-w-0 flex-1 hover:underline">
                <span className="block truncate font-medium">{payment.customerName}</span>
                <span className="block text-xs text-muted-foreground">
                  {paymentModeLabel(payment.mode)} · {payment.invoiceNumber}
                </span>
              </Link>
              <span className="text-right">
                <span className="block font-medium tabular-nums">{formatINR(payment.amount)}</span>
                <span className="block text-xs text-muted-foreground">{formatBusinessDate(payment.paymentDate)}</span>
              </span>
            </li>
          ))}
        </Rows>
      </CardContent>
    </Card>
  )
}

function ActionRow({ icon: Icon, count, label, to, detail, loading }: { icon: typeof Timer; count: number | undefined; label: string; to: string; detail?: string; loading: boolean }) {
  if (loading) return <Skeleton className="h-12 w-full" />
  return (
    <Link to={to} className="flex items-center gap-3 rounded-lg p-2 text-sm hover:bg-accent">
      <span className={cn('flex size-9 shrink-0 items-center justify-center rounded-lg', count ? 'bg-amber-500/15 text-amber-700 dark:text-amber-300' : 'bg-muted text-muted-foreground')}>
        <Icon className="size-4" aria-hidden />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block font-medium">
          <span className="tabular-nums">{(count ?? 0).toLocaleString('en-IN')}</span> {label}
        </span>
        {detail && <span className="block truncate text-xs text-muted-foreground">{detail}</span>}
      </span>
    </Link>
  )
}

const supplierNames = (items: { supplierName: string }[] | undefined) => (items?.length ? [...new Set(items.map((i) => i.supplierName))].join(', ') : undefined)

function OrdersAwaitingApproval() {
  const orders = usePurchaseOrders({ status: 'PendingApproval', page: 1, pageSize: 3 })
  return (
    <ActionRow icon={ShoppingBag} loading={orders.isPending} count={orders.data?.totalCount} label="purchase orders waiting for approval" to="/purchase-orders?view=PendingApproval" detail={supplierNames(orders.data?.items)} />
  )
}

function BillsNeedingAction() {
  const onHold = useSupplierBills({ matchStatus: 'NeedsApproval', unpaidOnly: true, page: 1, pageSize: 3 })
  const overdue = useSupplierBills({ overdueOnly: true, page: 1, pageSize: 3 })
  return (
    <>
      <ActionRow icon={FileWarning} loading={onHold.isPending} count={onHold.data?.totalCount} label="bills on hold (don't match the order)" to="/supplier-bills?view=approval" detail={supplierNames(onHold.data?.items)} />
      <ActionRow
        icon={Timer}
        loading={overdue.isPending}
        count={overdue.data?.totalCount}
        label="bills past their due date"
        to="/supplier-bills?view=overdue"
        detail={overdue.data?.items.length ? `${formatINRCompact(overdue.data.items.reduce((sum, b) => sum + b.balance, 0))} on the oldest three` : undefined}
      />
    </>
  )
}

/** Purchase orders waiting for approval, and supplier bills on hold or overdue: only the parts this person may see. */
export function PurchasesCard() {
  const { can } = useAuth()
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Purchases needing action</CardTitle>
        <CardDescription>What's waiting on someone</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-1">
        {can(Permissions.PurchaseOrdersView) && <OrdersAwaitingApproval />}
        {can(Permissions.SupplierBillsView) && <BillsNeedingAction />}
      </CardContent>
    </Card>
  )
}

/** Big buttons for everyday work, each only for people allowed to do it. */
export function QuickActionsCard() {
  const { can } = useAuth()
  const ai = useAiStatus()
  const [paying, setPaying] = useState(false)
  const tile = 'flex h-20 flex-col items-center justify-center gap-1.5 rounded-xl border bg-card text-sm font-medium transition-colors hover:border-primary/40 hover:bg-accent'
  const actions = [
    can(Permissions.OrdersEdit) && (
      <Link key="order" to="/orders/new" className={tile}>
        <ClipboardPlus className="size-5 text-primary" aria-hidden /> New order
      </Link>
    ),
    can(Permissions.SmartEntryUse) && ai.data?.enabled && (
      <Link key="smart" to="/orders/smart-entry" className={tile}>
        <ScanText className="size-5 text-primary" aria-hidden /> Smart order entry
      </Link>
    ),
    can(Permissions.PaymentsEdit) && (
      <button key="pay" type="button" className={tile} onClick={() => setPaying(true)}>
        <HandCoins className="size-5 text-primary" aria-hidden /> Record payment
      </button>
    ),
    can(Permissions.CustomersEdit) && (
      <Link key="customer" to="/customers/new" className={tile}>
        <UserPlus className="size-5 text-primary" aria-hidden /> New customer
      </Link>
    ),
  ].filter(Boolean)

  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Quick actions</CardTitle>
        <CardDescription>What you do most, one tap away</CardDescription>
      </CardHeader>
      <CardContent>
        {actions.length ? <div className="grid grid-cols-2 gap-2">{actions}</div> : <p className="text-sm text-muted-foreground">Nothing here for your role yet.</p>}
      </CardContent>
      {can(Permissions.PaymentsEdit) && <RecordPaymentPicker open={paying} onOpenChange={setPaying} />}
    </Card>
  )
}

/** The latest notifications, as in the bell: open one to go to it. */
export function NotificationsCard() {
  const navigate = useNavigate()
  const notifications = useNotifications()
  const markRead = useMarkRead()
  const markAllRead = useMarkAllRead()
  const data = notifications.data
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Notifications</CardTitle>
        <CardDescription>{data?.unreadCount ? `${data.unreadCount} unread` : 'All caught up'}</CardDescription>
        <CardAction>
          <Button variant="ghost" size="sm" disabled={!data?.unreadCount} onClick={() => data?.items[0] && markAllRead.mutate(data.items[0].id)}>
            <CheckCheck /> Mark all read
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="-mx-3">
        {notifications.isPending ? (
          <div className="flex flex-col gap-3 px-3">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : (
          <NotificationList
            items={(data?.items ?? []).slice(0, 6)}
            onOpen={(item) => {
              if (!item.read) markRead.mutate(item.id)
              navigate(item.link)
            }}
            emptyText="Nothing yet. Payments, orders and anything waiting for you will show up here."
          />
        )}
      </CardContent>
    </Card>
  )
}
