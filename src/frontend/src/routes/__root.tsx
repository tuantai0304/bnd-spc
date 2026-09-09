import { Link, Outlet, createRootRoute } from '@tanstack/react-router';

const NAV = [
  { to: '/', label: 'Fleet' },
  { to: '/call', label: 'Call a shuttle' },
  { to: '/history', label: 'History' },
  { to: '/stats', label: 'Stats' },
] as const;

function RootLayout() {
  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-10 border-b border-edge bg-void/90 backdrop-blur">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
          <Link to="/" className="text-base font-semibold tracking-tight">
            Space Travel
            <span className="ml-2 text-xs font-normal text-muted">Federation dispatch</span>
          </Link>

          <nav className="flex flex-wrap items-center gap-1">
            {NAV.map(({ to, label }) => (
              <Link
                key={to}
                to={to}
                // `exact` on "/" only, or every route would highlight it too.
                activeOptions={{ exact: to === '/' }}
                className="rounded-md px-3 py-1.5 text-sm text-muted hover:bg-raised hover:text-ink"
                activeProps={{ className: 'bg-raised text-ink' }}
              >
                {label}
              </Link>
            ))}
          </nav>
        </div>
      </header>

      <main className="mx-auto max-w-7xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}

export const Route = createRootRoute({
  component: RootLayout,
  notFoundComponent: () => (
    <div className="py-20 text-center">
      <p className="text-lg font-medium">Page not found.</p>
      <Link to="/" className="mt-3 inline-block text-sm text-accent hover:underline">
        ← Back to the fleet
      </Link>
    </div>
  ),
});
