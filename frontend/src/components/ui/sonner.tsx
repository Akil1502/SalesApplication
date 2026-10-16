import { useContext } from "react"
import { Toaster as Sonner, type ToasterProps } from "sonner"
import { PreferencesContext } from "@/components/fx/preferences-context"
import { InfoIcon, TriangleAlertIcon, OctagonXIcon, Loader2Icon } from "lucide-react"
import { AnimatedCheck } from "@/components/fx/AnimatedCheck"

// The shadcn template reads the theme from next-themes; this app follows the user's light/dark choice.
const Toaster = ({ theme, ...props }: ToasterProps) => {
  const resolvedTheme = useContext(PreferencesContext)?.resolvedTheme
  return (
    <Sonner
      theme={theme ?? resolvedTheme ?? "system"}
      className="toaster group"
      icons={{
        success: (
          <AnimatedCheck className="size-4" />
        ),
        info: (
          <InfoIcon className="size-4" />
        ),
        warning: (
          <TriangleAlertIcon className="size-4" />
        ),
        error: (
          <OctagonXIcon className="size-4" />
        ),
        loading: (
          <Loader2Icon className="size-4 animate-spin" />
        ),
      }}
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
          "--border-radius": "var(--radius)",
        } as React.CSSProperties
      }
      toastOptions={{
        classNames: {
          toast: "cn-toast",
        },
      }}
      {...props}
    />
  )
}

export { Toaster }
