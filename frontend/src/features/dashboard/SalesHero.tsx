import { ChartColumn, PartyPopper } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { confettiBurst } from '@/components/fx/confetti'
import { useResolvedTheme } from '@/components/fx/preferences-context'
import { SalesCity3D } from '@/components/three/scenes'
import { Button } from '@/components/ui/button'
import { formatINR, formatINRCompact, todayInIndia } from '@/lib/format'
import { t, tn } from '@/lib/i18n'
import { isRecordMonth, toTrendPoint, type MonthlySales, type TrendPoint } from './trend'

/** The still version of the city: the months as a soft skyline along the bottom. */
function StillSkyline({ points }: { points: TrendPoint[] }) {
  const max = Math.max(...points.map((p) => p.value), 1)
  const width = 100 / Math.max(points.length, 1)
  return (
    <svg aria-hidden viewBox="0 0 100 40" preserveAspectRatio="none" className="absolute inset-x-0 bottom-0 h-1/2 w-full opacity-60">
      <defs>
        <linearGradient id="still-skyline" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stopColor="var(--brand-2)" />
          <stop offset="1" stopColor="var(--brand)" stopOpacity="0.3" />
        </linearGradient>
      </defs>
      {points.map((p, i) => {
        const height = 4 + (p.value / max) * 34
        return <rect key={p.month} x={i * width + width * 0.15} y={40 - height} width={width * 0.7} height={height} rx="0.6" fill="url(#still-skyline)" opacity={p.partial ? 0.5 : 1} />
      })}
    </svg>
  )
}

/**
 * The top of the dashboard: a greeting over a small 3D city whose buildings are the last 24 months of sales. A
 * month that already beats every earlier one gets a burst of confetti, once.
 */
export function SalesHero({ greeting, description, months }: { greeting: string; description: ReactNode; months: MonthlySales[] | undefined }) {
  const dark = useResolvedTheme() === 'dark'
  const [hovered, setHovered] = useState<number | null>(null)
  const currentMonth = todayInIndia().slice(0, 7)
  const points = (months ?? []).map((m) => toTrendPoint(m, currentMonth))
  const point = hovered === null ? undefined : points[hovered]
  const record = isRecordMonth(points)

  useEffect(() => {
    if (!record) return
    const key = `salesapp.celebrated.${currentMonth}`
    try {
      if (localStorage.getItem(key)) return
      localStorage.setItem(key, 'yes')
    } catch {
      return
    }
    const timer = setTimeout(() => confettiBurst(window.innerWidth / 2, 180, { count: 130, spread: 1.4 }), 900)
    return () => clearTimeout(timer)
  }, [record, currentMonth])

  return (
    <section
      className="relative h-[320px] overflow-hidden rounded-2xl border shadow-[var(--shadow-elevated)] md:h-[380px]"
      style={{
        background: dark
          ? 'radial-gradient(circle at 70% 20%, color-mix(in oklch, var(--brand) 30%, transparent), transparent 60%), linear-gradient(180deg, var(--card), color-mix(in oklch, var(--background) 85%, black))'
          : 'radial-gradient(circle at 70% 15%, color-mix(in oklch, var(--brand-2) 22%, transparent), transparent 60%), linear-gradient(180deg, var(--background), var(--accent))',
      }}
    >
      {points.length > 0 && (
        <SalesCity3D
          className="absolute inset-0"
          months={points.map((p) => ({ tick: p.tick, value: p.value, partial: p.partial }))}
          dark={dark}
          formatValue={(v) => formatINRCompact(v)}
          onHover={setHovered}
          fallback={<StillSkyline points={points} />}
        />
      )}
      {/* A soft wash behind the text keeps it readable over the city. */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-y-0 left-0 w-full md:w-1/2"
        style={{ background: `linear-gradient(90deg, color-mix(in oklch, var(--background) 85%, transparent), transparent)` }}
      />

      <div className="pointer-events-none relative z-10 flex h-full flex-col justify-between p-5 md:p-7">
        <div className="flex items-start justify-between gap-4">
          <div className="max-w-md space-y-2">
            <span aria-hidden className="block h-1 w-10 rounded-full" style={{ background: 'var(--gradient-brand)' }} />
            <h2 className="text-2xl font-semibold tracking-tight text-balance md:text-3xl">{greeting}</h2>
            <div className="text-sm text-muted-foreground">{description}</div>
          </div>
          <Button asChild variant="outline" className="pointer-events-auto bg-background/70 backdrop-blur">
            <Link to="/reports">
              <ChartColumn /> {t('Reports')}
            </Link>
          </Button>
        </div>

        <div className="flex flex-wrap items-end justify-between gap-2 text-xs">
          <p className="fx-glass rounded-lg border px-3 py-1.5" aria-live="polite">
            {point ? (
              <>
                <span className="font-semibold tabular-nums">{formatINR(point.value, 0)}</span>
                <span className="text-muted-foreground">
                  {' '}
                  · {point.partial ? t('{month} so far', { month: point.month }) : point.month} · {tn(point.invoices, '{count} invoice', '{count} invoices')}
                </span>
              </>
            ) : (
              <span className="text-muted-foreground">{t('Each building is a month of sales. Drag to turn the city; point at one for its figures.')}</span>
            )}
          </p>
          {record && (
            <span className="fx-pop inline-flex items-center gap-1.5 rounded-full bg-amber-500/90 px-3 py-1 font-medium text-white shadow-md">
              <PartyPopper className="size-3.5" /> {t("Best month yet, and it isn't over")}
            </span>
          )}
        </div>
      </div>
    </section>
  )
}
