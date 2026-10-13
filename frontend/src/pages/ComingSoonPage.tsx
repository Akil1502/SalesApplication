import { Construction } from 'lucide-react'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

export function ComingSoonPage({ title, phase }: { title: string; phase?: string }) {
  return (
    <Card className="max-w-lg">
      <CardHeader>
        <Construction className="mb-2 size-6 text-muted-foreground" />
        <CardTitle>{title}</CardTitle>
        <CardDescription>
          This screen is part of {phase ? `phase ${phase}` : 'a later phase'} of the rebuild and isn't built yet.
        </CardDescription>
      </CardHeader>
    </Card>
  )
}
