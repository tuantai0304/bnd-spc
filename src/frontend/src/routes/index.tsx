import { useEffect } from 'react';
import { Link, createFileRoute } from '@tanstack/react-router';
import { CapacityBar } from '../components/CapacityBar';
import { Countdown } from '../components/Countdown';
import { ErrorBanner } from '../components/ErrorBanner';
import { StatusBadge } from '../components/StatusBadge';
import { useFleetStore } from '../stores/useFleetStore';
import type { Shuttle } from '../api/types';

export const Route = createFileRoute('/')({ component: FleetDashboard });

function FleetDashboard() {
  const { data, loading, error, updatedAt, refresh, startPolling, stopPolling } =
    useFleetStore();

  // 1000 ms while the page is open, and never longer: calls can be placed from
  // anywhere, so an idle fleet can become busy with no action here.
  useEffect(() => {
    startPolling();
    return stopPolling;
  }, [startPolling, stopPolling]);

  if (loading && !data) return <p className="py-20 text-center text-muted">Loading fleet…</p>;

  // Only a *first* load failure earns the whole page. Once we have shuttles on
  // screen, a failed poll is a banner and the stale cards stay put.
  if (error && !data) {
    return (
      <div className="py-16">
        <ErrorBanner error={error} onRetry={() => void refresh()} />
      </div>
    );
  }

  const shuttles = data ?? [];
  const idle = shuttles.filter((s) => s.state === 'Idle').length;
  const enRoute = shuttles.filter((s) => s.state === 'EnRoute').length;
  const aboard = shuttles.reduce((n, s) => n + s.lifeFormsAboard, 0);
  const waiting = shuttles.reduce(
    (n, s) => n + s.manifest.filter((m) => m.status === 'Assigned').length,
    0,
  );

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Fleet</h1>
          <p className="mt-1 text-sm text-muted">
            {shuttles.length} shuttles · {idle} idle · {enRoute} en route · {aboard} life
            forms aboard
            {waiting > 0 && ` · ${waiting} party(s) loading`}
          </p>
        </div>
        <p className="text-xs text-muted">
          {updatedAt ? `Updated ${updatedAt.toLocaleTimeString()}` : 'Updating…'}
        </p>
      </header>

      {error && data && (
        <ErrorBanner
          error={error}
          variant="warning"
          dismissible
          onRetry={() => void refresh()}
        />
      )}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {shuttles.map((s) => (
          <ShuttleCard key={s.id} shuttle={s} />
        ))}
      </div>

      <p className="text-xs text-muted">
        Shuttles are seeded at startup and moved only by the simulation — this view is
        read-only. Place a call from{' '}
        <Link to="/call" className="text-accent hover:underline">
          Call a shuttle
        </Link>
        .
      </p>
    </div>
  );
}

function ShuttleCard({ shuttle: s }: { shuttle: Shuttle }) {
  // Derive the ceilings rather than hardcoding 20 / 4000: they are config
  // (Fleet:MaxLifeFormsPerShuttle) and could change without a frontend deploy.
  const lifeFormCap = s.lifeFormsAboard + s.remainingLifeForms;
  const weightCap = s.weightAboardKg + s.remainingWeightKg;

  return (
    <article className="flex flex-col gap-4 rounded-xl border border-edge bg-panel p-4">
      <div className="flex items-start justify-between gap-2">
        <h2 className="font-semibold">{s.name}</h2>
        <StatusBadge status={s.state} size="sm" />
      </div>

      <div className="space-y-1 text-sm">
        <p className="text-muted">
          {s.state === 'EnRoute' ? (
            <>
              {s.currentPlanet ?? '—'} <span className="text-edge">→</span>{' '}
              <span className="text-ink">{s.flyingToPlanet}</span>
            </>
          ) : (
            <>At <span className="text-ink">{s.currentPlanet ?? '—'}</span></>
          )}
        </p>

        {/* flyingToPlanet and tripDestination diverge on a repositioning leg —
            an empty shuttle flying out to collect a party. Showing both is what
            makes the dispatcher's "smarts" visible rather than implied. */}
        {s.tripDestination && (
          <p className="text-muted">
            Carrying to <span className="text-ink">{s.tripDestination}</span>
          </p>
        )}

        {s.secondsUntilArrival !== null && (
          <p className="text-enroute">
            Arrives in <Countdown seconds={s.secondsUntilArrival} />
          </p>
        )}
      </div>

      <div className="space-y-2">
        <CapacityBar label="Life forms" used={s.lifeFormsAboard} capacity={lifeFormCap} />
        <CapacityBar
          label="Weight"
          used={s.weightAboardKg}
          capacity={weightCap}
          unit=" kg"
        />
      </div>

      {s.manifest.length > 0 && (
        <div className="border-t border-edge pt-3">
          {/* More than one entry means the dispatcher batched two calls onto one
              launch — the single largest fuel saving the fleet can make. */}
          <p className="mb-2 text-xs text-muted">
            Manifest
            {s.manifest.length > 1 && (
              <span className="ml-2 rounded bg-arrived/15 px-1.5 py-0.5 text-arrived">
                {s.manifest.length} calls batched
              </span>
            )}
          </p>
          <table className="w-full text-xs">
            <thead className="text-muted">
              <tr className="text-left">
                <th className="pb-1 font-normal">Call</th>
                <th className="pb-1 font-normal">Status</th>
                <th className="pb-1 text-right font-normal">Life forms</th>
                <th className="pb-1 text-right font-normal">Weight</th>
              </tr>
            </thead>
            <tbody>
              {s.manifest.map((m) => (
                <tr key={m.travelRequestId} className="border-t border-edge/50">
                  <td className="py-1.5">
                    <Link
                      to="/requests/$id"
                      params={{ id: String(m.travelRequestId) }}
                      className="text-accent hover:underline"
                    >
                      #{m.travelRequestId}
                    </Link>
                  </td>
                  <td className="py-1.5">
                    <StatusBadge status={m.status} size="sm" />
                  </td>
                  <td className="tnum py-1.5 text-right">{m.lifeFormCount}</td>
                  <td className="tnum py-1.5 text-right">{m.totalWeightKg} kg</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </article>
  );
}
