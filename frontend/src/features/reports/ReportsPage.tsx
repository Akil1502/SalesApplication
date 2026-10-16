import { CalendarRange, ChevronRight, Clock, FileSpreadsheet, Landmark, Package, Undo2, Users, Warehouse, type LucideIcon } from 'lucide-react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/PageHeader'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { errorMessage } from '@/lib/api/errors'
import { useReportDefinitions } from './api'

const icons: Record<string, LucideIcon> = {
  'sales-by-period': CalendarRange,
  'sales-by-customer': Users,
  'sales-by-product': Package,
  'gst-summary': Landmark,
  'hsn-summary': FileSpreadsheet,
  'receivables-ageing': Clock,
  stock: Warehouse,
  'credit-notes': Undo2,
  'supplier-returns': Undo2,
}

export function ReportsPage() {
  const definitions = useReportDefinitions()

  return (
    <>
      <PageHeader title="Reports" description="Every report can be filtered by date where it makes sense, and downloaded as an Excel file." />

      {definitions.error && (
        <Alert variant="destructive">
          <AlertDescription>{errorMessage(definitions.error)}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {definitions.isPending
          ? Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-28 rounded-xl" />)
          : definitions.data?.map((report) => {
              const Icon = icons[report.key] ?? FileSpreadsheet
              return (
                <Link
                  key={report.key}
                  to={`/reports/${report.key}`}
                  className="group rounded-xl focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                >
                  <Card className="h-full transition-colors group-hover:bg-muted/40">
                    <CardHeader>
                      <CardTitle className="flex items-center gap-2">
                        <Icon className="size-4 text-muted-foreground" aria-hidden />
                        {report.title}
                        <ChevronRight className="ml-auto size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" aria-hidden />
                      </CardTitle>
                      <CardDescription>{report.description}</CardDescription>
                    </CardHeader>
                  </Card>
                </Link>
              )
            })}
      </div>
    </>
  )
}
