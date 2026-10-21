import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { t } from '@/lib/i18n'

interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, pageSize, totalCount, totalPages, onPageChange }: PaginationProps) {
  if (totalCount === 0) return null
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalCount)

  return (
    <div className="flex items-center justify-between gap-4 text-sm text-muted-foreground">
      <span>
        {t('{first}–{last} of {total}', { first: first.toLocaleString('en-IN'), last: last.toLocaleString('en-IN'), total: totalCount.toLocaleString('en-IN') })}
      </span>
      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft />
          {t('Previous')}
        </Button>
        <span className="tabular-nums">
          {t('Page {page} of {pages}', { page, pages: Math.max(totalPages, 1) })}
        </span>
        <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          {t('Next')}
          <ChevronRight />
        </Button>
      </div>
    </div>
  )
}
