import { motion } from 'motion/react'
import { Link } from 'react-router'
import { AuroraBackground } from '@/components/fx/AuroraBackground'
import { EmptyArt } from '@/components/fx/EmptyArt'
import { Button } from '@/components/ui/button'

export function NotFoundPage() {
  return (
    <main className="relative isolate flex min-h-svh flex-col items-center justify-center gap-4 overflow-hidden p-4 text-center">
      <AuroraBackground />
      <EmptyArt className="h-32 w-44" />
      <motion.p
        className="fx-gradient-text text-7xl font-bold tracking-tighter"
        initial={{ opacity: 0, y: 20, rotateX: 40 }}
        animate={{ opacity: 1, y: 0, rotateX: 0 }}
        transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
        style={{ transformPerspective: 600 }}
      >
        404
      </motion.p>
      <h1 className="text-2xl font-semibold">Page not found</h1>
      <p className="text-muted-foreground">The page you're looking for doesn't exist or has moved.</p>
      <Button asChild variant="outline">
        <Link to="/">Back to dashboard</Link>
      </Button>
    </main>
  )
}
