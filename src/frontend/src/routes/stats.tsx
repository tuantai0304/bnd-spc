import { useCallback, useEffect, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { ApiError } from '../api/client';
import { getPlanetStats } from '../api/endpoints';
import { ErrorBanner } from '../components/ErrorBanner';
import { usePlanetsStore } from '../stores/usePlanetsStore';
import type { PlanetStats } from '../api/types';

export const Route = createFileRoute('/stats')({ component: Stats });

const RANK_LABEL = (rank: number, of: number) =>
  `#${rank}${rank === 1 ? ' (closest)' : rank === of ? ' (farthest)' : ''}`;

function Stats() {
  // Stats are local to this page: there is no polling lifecycle to own and no
  // other page reads them, so a store would be indirection for its own sake.
  const [data, setData] = useState<PlanetStats[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const planets = usePlanetsStore();
  const loadPlanets = planets.startPolling;

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await getPlanetStats());
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    loadPlanets();
  }, [refresh, loadPlanets]);

  if (loading && !data) return <p className="py-20 text-center text-muted">Loading stats…</p>;

  if (error && !data) {
    return (
      <div className="py-16">
        <ErrorBanner error={error} onRetry={() => void refresh()} />
      </div>
    );
  }

  const rows = data ?? [];
  const totalCompleted = rows.reduce((n, r) => n + r.completedTrips, 0);
  const totalRejected = rows.reduce((n, r) => n + r.rejectedCalls, 0);
  const totalDelivered = rows.reduce((n, r) => n + r.lifeFormsDelivered, 0);
  // Rows arrive ordered completedTrips DESC, so the first row is the busiest.
  const busiest = rows[0];
  const maxTrips = Math.max(1, ...rows.map((r) => r.completedTrips));
  const maxRank = Math.max(1, ...rows.map((r) => r.distanceRank));

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Planet stats</h1>
          <p className="mt-1 text-sm text-muted">
            Trips grouped by destination — where the next shuttle would earn its keep.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void refresh()}
          className="rounded-md border border-edge px-3 py-1.5 text-sm hover:bg-raised"
        >
          Refresh
        </button>
      </header>

      {error && data && <ErrorBanner error={error} variant="warning" dismissible />}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Tile label="Completed trips" value={totalCompleted} />
        <Tile label="Rejected calls" value={totalRejected} tone={totalRejected > 0 ? 'rejected' : undefined} />
        <Tile label="Life forms delivered" value={totalDelivered} />
        <Tile
          label="Busiest destination"
          value={busiest && busiest.completedTrips > 0 ? busiest.planet : '—'}
        />
      </div>

      <section className="overflow-x-auto rounded-xl border border-edge bg-panel">
        <table className="w-full min-w-[46rem] text-sm">
          <thead className="text-muted">
            <tr className="border-b border-edge">
              <th className="px-3 py-2 text-left font-normal">Planet</th>
              <th className="px-3 py-2 text-left font-normal">Distance</th>
              <th className="px-3 py-2 text-right font-normal">Completed trips</th>
              <th className="px-3 py-2 text-right font-normal">Rejected calls</th>
              <th className="px-3 py-2 text-right font-normal">Life forms delivered</th>
              <th className="px-3 py-2 text-right font-normal">Weight delivered</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr
                key={r.planetId}
                // A dock that keeps turning parties away answers the brief's
                // question directly, so it is called out rather than buried.
                className={`border-b border-edge/40 last:border-0 ${
                  r.rejectedCalls > 0 ? 'bg-rejected/5' : ''
                }`}
              >
                <td className="px-3 py-2 font-medium">{r.planet}</td>
                <td className="px-3 py-2 text-muted">{RANK_LABEL(r.distanceRank, maxRank)}</td>
                <td className="px-3 py-2">
                  <div className="flex items-center justify-end gap-3">
                    <div className="h-1.5 w-24 overflow-hidden rounded-full bg-raised">
                      <div
                        className="h-full rounded-full bg-arrived"
                        style={{ width: `${(r.completedTrips / maxTrips) * 100}%` }}
                      />
                    </div>
                    <span className="tnum w-6 text-right">{r.completedTrips}</span>
                  </div>
                </td>
                <td
                  className={`tnum px-3 py-2 text-right ${
                    r.rejectedCalls > 0 ? 'font-medium text-rejected' : ''
                  }`}
                >
                  {r.rejectedCalls}
                </td>
                <td className="tnum px-3 py-2 text-right">{r.lifeFormsDelivered}</td>
                <td className="tnum px-3 py-2 text-right">{r.weightDeliveredKg} kg</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      {/* An empty database still returns all five planets with zeroes, so there
          is no empty array to detect — only an all-zero table. */}
      {totalCompleted === 0 && (
        <p className="text-sm text-muted">
          No trips recorded yet. Every planet is listed with zeroes so a dock nobody travels
          to is still visible.
        </p>
      )}

      <section className="rounded-xl border border-edge bg-panel">
        <h2 className="border-b border-edge px-4 py-3 text-sm font-medium">Planets</h2>
        {planets.error ? (
          <div className="p-4">
            <ErrorBanner error={planets.error} onRetry={() => void planets.refresh()} />
          </div>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-muted">
              <tr>
                <th className="px-4 py-2 text-left font-normal">Id</th>
                <th className="px-4 py-2 text-left font-normal">Name</th>
                <th className="px-4 py-2 text-left font-normal">Distance rank</th>
              </tr>
            </thead>
            <tbody>
              {(planets.data ?? []).map((p) => (
                <tr key={p.id} className="border-t border-edge/40">
                  <td className="tnum px-4 py-2 text-muted">{p.id}</td>
                  <td className="px-4 py-2">{p.name}</td>
                  <td className="tnum px-4 py-2 text-muted">{p.distanceRank}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function Tile({
  label,
  value,
  tone,
}: {
  label: string;
  value: string | number;
  tone?: 'rejected';
}) {
  return (
    <div className="rounded-xl border border-edge bg-panel p-4">
      <p className="text-xs text-muted">{label}</p>
      <p
        className={`tnum mt-1 text-2xl font-semibold ${
          tone === 'rejected' ? 'text-rejected' : ''
        }`}
      >
        {value}
      </p>
    </div>
  );
}
