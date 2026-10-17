import { motion } from 'motion/react'
import { NavLink } from 'react-router'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { cn } from '@/lib/utils'
import { useBrandName } from '@/features/branding/api'
import { BrandMark } from './BrandMark'
import { useNav } from './use-nav'

/** On phones every layout uses this: the full menu, sliding in from the left (opened from the bottom bar's Menu). */
export function MobileNav({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { groups, active } = useNav()
  const name = useBrandName()

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="left" className="w-72 gap-0 bg-sidebar p-0 text-sidebar-foreground">
        <div className="flex h-14 items-center gap-3 border-b border-sidebar-border px-4">
          <BrandMark className="size-8" />
          <div className="min-w-0">
            <SheetTitle className="truncate text-sidebar-foreground">{name}</SheetTitle>
            <SheetDescription className="text-xs text-sidebar-foreground/60">Sales &amp; GST billing</SheetDescription>
          </div>
        </div>
        <nav aria-label="Main menu" className="flex-1 overflow-y-auto p-3">
          {groups.map((group, groupIndex) => (
            <div key={group.label} className="mb-3">
              <p className="px-2 pb-1 text-[11px] font-medium tracking-wide text-sidebar-foreground/65 uppercase">{group.label}</p>
              {group.items.map((item, index) => (
                <motion.div key={item.to} initial={{ opacity: 0, x: -14 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: 0.02 * (groupIndex * 3 + index) }}>
                  <NavLink
                    to={item.to}
                    end
                    className={cn(
                      'flex h-10 items-center gap-3 rounded-xl px-3 text-sm',
                      active?.to === item.to ? 'bg-sidebar-accent text-sidebar-accent-foreground' : 'text-sidebar-foreground/80 hover:bg-sidebar-accent/60',
                    )}
                  >
                    <item.icon className="size-[18px]" />
                    {item.title}
                  </NavLink>
                </motion.div>
              ))}
            </div>
          ))}
        </nav>
      </SheetContent>
    </Sheet>
  )
}
