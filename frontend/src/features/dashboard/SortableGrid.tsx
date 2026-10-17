import {
  closestCenter,
  DndContext,
  PointerSensor,
  TouchSensor,
  useSensor,
  useSensors,
  type Announcements,
  type UniqueIdentifier,
} from '@dnd-kit/core'
import { rectSortingStrategy, SortableContext, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { GripVertical } from 'lucide-react'
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface SortableGridProps<T extends { id: string }> {
  items: T[]
  editing: boolean
  /** The item's name, for the handle's label and what screen readers hear while moving it. */
  titleOf: (id: string) => string
  onMove: (id: string, overId: string) => void
  className?: string
  itemClassName?: (item: T) => string | undefined
  renderItem: (item: T) => ReactNode
  /** Buttons beside the handle while customising (resize, hide). */
  renderControls?: (item: T) => ReactNode
}

const STEPS: Record<string, number> = { ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1, Home: -Infinity, End: Infinity }

interface SortableItemProps {
  id: string
  editing: boolean
  title: string
  className?: string
  controls?: ReactNode
  /** Arrow keys on the handle move the item one place earlier or later; Home and End to the start or end. */
  onStep: (step: number) => void
  children: ReactNode
}

function SortableItem({ id, editing, title, className, controls, onStep, children }: SortableItemProps) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({ id, disabled: !editing })
  return (
    <div
      ref={setNodeRef}
      data-dashboard-item
      data-dragging={isDragging || undefined}
      className={cn('relative', className, editing && 'rounded-2xl outline-2 outline-offset-4 outline-primary/35 outline-dashed', isDragging && 'z-30')}
      style={{ transform: CSS.Translate.toString(transform), transition }}
    >
      {editing && (
        <div className="absolute -top-4 right-3 z-10 flex items-center gap-0.5 rounded-full border bg-popover p-0.5 text-popover-foreground shadow-md">
          <button
            ref={setActivatorNodeRef}
            type="button"
            {...attributes}
            {...listeners}
            data-handle-for={id}
            onKeyDown={(event: KeyboardEvent) => {
              const step = STEPS[event.key]
              if (step === undefined) return
              event.preventDefault()
              onStep(step)
            }}
            aria-label={`Move ${title}`}
            title="Drag to move"
            className="flex size-8 cursor-grab touch-none items-center justify-center rounded-full hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none active:cursor-grabbing"
          >
            <GripVertical className="size-4" />
          </button>
          {controls}
        </div>
      )}
      {/* While customising, the cards are pictures of themselves: nothing inside can be clicked by accident. */}
      <div inert={editing} className={cn('h-full transition-[scale,box-shadow] duration-200', editing && 'select-none', isDragging && 'scale-[1.02] rounded-2xl shadow-[var(--shadow-lifted)]')}>
        {children}
      </div>
    </div>
  )
}

/**
 * A grid whose items can be put in a new order while customising: dragged by mouse or finger, or moved with the
 * arrow keys on their handle (one place earlier or later in reading order, which works the same in a row or a grid).
 */
export function SortableGrid<T extends { id: string }>({ items, editing, titleOf, onMove, className, itemClassName, renderItem, renderControls }: SortableGridProps<T>) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 150, tolerance: 6 } }),
  )
  const [said, setSaid] = useState('')
  // Reordering moves the item's element, which can lose keyboard focus: put it back on the handle.
  const refocus = useRef<string | null>(null)
  useEffect(() => {
    if (!refocus.current) return
    document.querySelector<HTMLElement>(`[data-handle-for="${refocus.current}"]`)?.focus()
    refocus.current = null
  }, [items])

  const step = (id: string, by: number) => {
    const from = items.findIndex((item) => item.id === id)
    const to = Math.min(items.length - 1, Math.max(0, from + by))
    if (from < 0 || to === from) return setSaid(`${titleOf(id)} is already ${by < 0 ? 'first' : 'last'}.`)
    refocus.current = id
    onMove(id, items[to]!.id)
    setSaid(`${titleOf(id)} moved to place ${to + 1} of ${items.length}.`)
  }
  const name = (id: UniqueIdentifier) => titleOf(String(id))
  const announcements: Announcements = {
    onDragStart: ({ active }) => `Picked up ${name(active.id)}.`,
    onDragOver: ({ active, over }) => (over ? `${name(active.id)} is over ${name(over.id)}.` : `${name(active.id)} isn't over another card.`),
    onDragEnd: ({ active, over }) => (over && over.id !== active.id ? `${name(active.id)} moved to where ${name(over.id)} was.` : `${name(active.id)} dropped.`),
    onDragCancel: ({ active }) => `Moving ${name(active.id)} was cancelled.`,
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      accessibility={{
        announcements,
        screenReaderInstructions: { draggable: 'Drag it with the mouse or a finger, or use the arrow keys to move it one place earlier or later.' },
      }}
      onDragEnd={({ active, over }) => over && active.id !== over.id && onMove(String(active.id), String(over.id))}
    >
      <SortableContext items={items.map((item) => item.id)} strategy={rectSortingStrategy}>
        <div className={className}>
          {items.map((item) => (
            <SortableItem
              key={item.id}
              id={item.id}
              editing={editing}
              title={titleOf(item.id)}
              className={itemClassName?.(item)}
              controls={editing ? renderControls?.(item) : undefined}
              onStep={(by) => step(item.id, by)}
            >
              {renderItem(item)}
            </SortableItem>
          ))}
        </div>
        <p className="sr-only" aria-live="polite">
          {said}
        </p>
      </SortableContext>
    </DndContext>
  )
}
