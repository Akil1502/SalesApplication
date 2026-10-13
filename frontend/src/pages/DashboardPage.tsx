import { CheckCircle2, Circle } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { useAuth } from '@/features/auth/auth-context'

// Until the real dashboard lands in P4, the home page shows where the rebuild stands.
const roadmap = [
  { phase: 'P0', title: 'Foundation: sign-in, roles, app shell', done: true },
  { phase: 'P1', title: 'Customers, products, users, company settings', done: true },
  { phase: 'P2', title: 'Inventory: stock ledger and low-stock alerts', done: true },
  { phase: 'P3', title: 'Orders, GST invoices and payments', done: false },
  { phase: 'P4', title: 'Demo data, dashboard and reports', done: false },
  { phase: 'P5', title: 'AI smart order entry', done: false },
  { phase: 'P6', title: 'AI: ask your sales data', done: false },
  { phase: 'P7', title: 'AI assistant for drafting', done: false },
  { phase: 'P8', title: 'AI insights and forecasting', done: false },
]

function greeting(now = new Date()): string {
  const hour = now.getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 17) return 'Good afternoon'
  return 'Good evening'
}

export function DashboardPage() {
  const { user } = useAuth()

  return (
    <>
      <div>
        <h2 className="text-2xl font-semibold tracking-tight">
          {greeting()}, {user?.fullName}
        </h2>
        <p className="text-muted-foreground">Sales figures and KPIs appear here once orders and invoices exist (phase P4).</p>
      </div>

      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>Rebuild progress</CardTitle>
          <CardDescription>What's live, and what's coming next.</CardDescription>
        </CardHeader>
        <CardContent>
          <ul className="flex flex-col gap-3">
            {roadmap.map((step) => (
              <li key={step.phase} className="flex items-center gap-3 text-sm">
                {step.done ? (
                  <CheckCircle2 className="size-4 text-emerald-600" aria-label="Done" />
                ) : (
                  <Circle className="size-4 text-muted-foreground" aria-label="Planned" />
                )}
                <Badge variant="outline" className="w-9 justify-center">
                  {step.phase}
                </Badge>
                <span className={step.done ? '' : 'text-muted-foreground'}>{step.title}</span>
              </li>
            ))}
          </ul>
        </CardContent>
      </Card>
    </>
  )
}
