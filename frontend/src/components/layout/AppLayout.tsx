import { Outlet, useLocation } from 'react-router'
import { Separator } from '@/components/ui/separator'
import { SidebarInset, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { AssistantLauncher } from '@/features/assistant/AssistantLauncher'
import { AppSidebar } from './AppSidebar'
import { findNavItem } from './navigation'

function usePageTitle(): string {
  const { pathname } = useLocation()
  return findNavItem(pathname)?.title ?? 'SalesApp'
}

/** Signed-in shell: collapsible sidebar, top bar with the page title and the assistant, and the routed page. */
export function AppLayout() {
  const title = usePageTitle()

  return (
    <SidebarProvider>
      <AppSidebar />
      {/* min-w-0: a wide chart or table must not stop the page column shrinking, or the page scrolls sideways. */}
      <SidebarInset className="min-w-0">
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger className="-ml-1" />
          <Separator orientation="vertical" className="mr-2 data-[orientation=vertical]:h-4" />
          <h1 className="text-sm font-medium">{title}</h1>
          <div className="ml-auto">
            <AssistantLauncher />
          </div>
        </header>
        <div className="flex flex-1 flex-col gap-4 p-4 md:p-6">
          <Outlet />
        </div>
      </SidebarInset>
    </SidebarProvider>
  )
}
