import { useEffect } from 'react';
import { Link, createFileRoute, useNavigate } from '@tanstack/react-router';
import { ErrorBanner } from '../components/ErrorBanner';
import { PAGE_SIZES, Pager } from '../components/Pager';
import { StatusBadge } from '../components/StatusBadge';
import { formatUtc } from '../lib/dates';
import { useHistoryStore } from '../stores/useHistoryStore';

interface HistorySearch {
  page: number;
  pageSize: number;
}

export const Route = createFileRoute('/history')({
  // Clamping here means the UI can never itself send a value the backend would
  // reject — a 400 from this page therefore means a hand-edited URL.
  validateSearch: (s: Record<string, unknown>): HistorySearch => ({
    page: Math.max(1, Number(s.page ?? 1) || 1),
    pageSize: (PAGE_SIZES as readonly number[]).includes(Number(s.pageSize))
      ? Number(s.pageSize)
      : 25,
  }),
  component: History,
});

function History() {
  const { page, pageSize } = Route.useSearch();
  const navigate = useNavigate({ from: '/history' });
  const store = useHistoryStore();
  const { load, refresh } = store;

  // Paging lives in the URL, so the fetch is driven by the search params —
  // which makes every page shareable and the back button work for free.
  useEffect(() => {
    void load(page, pageSize);
  }, [page, pageSize, load]);

  const goToPage = (next: number) =>
    void navigate({ search: (prev) => ({ ...prev, page: next }) });

  // Resetting page is not optional: a user on page 4 at 10-per-page would
  // otherwise land past the end at 100-per-page.
  const changePageSize = (next: number) =>
    void navigate({ search: () => ({ page: 1, pageSize: next }) });

  const data = store.data;
  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;

  // Asking for a page past the end is not an error — the API returns empty
  // items with a truthful totalCount. Land the user on the last real page
  // rather than showing them a confusing empty table.
  useEffect(() => {
    if (!data || data.page !== page || data.pageSize !== pageSize) return;
    if (data.items.length > 0 || data.totalCount === 0) return;
    const lastPage = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    if (page > lastPage) {
      void navigate({ search: (prev) => ({ ...prev, page: lastPage }) });
    }
  }, [data, page, pageSize, navigate]);

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Travel history</h1>
          <p className="mt-1 text-sm text-muted">
            {totalCount} record{totalCount === 1 ? '' : 's'} · completed and rejected calls
            only, newest first
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

      {store.error && (
        <ErrorBanner
          error={store.error}
          onRetry={() => {
            // A 400 here can only come from a hand-edited URL; reset to the
            // defaults so the page is usable again rather than just retrying.
            if (store.error?.status === 400) {
              void navigate({ search: () => ({ page: 1, pageSize: 25 }) });
            } else {
              void refresh();
            }
          }}
        />
      )}

      {store.loading && !data ? (
        <p className="py-20 text-center text-muted">Loading history…</p>
      ) : totalCount === 0 ? (
        <div className="rounded-xl border border-edge bg-panel py-16 text-center">
          <p className="text-sm">No trips recorded yet.</p>
          <p className="mt-1 text-sm text-muted">
            Completed and rejected calls appear here.
          </p>
          <Link to="/call" className="mt-3 inline-block text-sm text-accent hover:underline">
            Call a shuttle →
          </Link>
        </div>
      ) : (
        <>
          {/* The table scrolls inside its own container so a narrow screen
              never forces the whole page sideways. */}
          <div className="overflow-x-auto rounded-xl border border-edge bg-panel">
            <table className="w-full min-w-[64rem] text-sm">
              <thead className="text-muted">
                <tr className="border-b border-edge">
                  <th className="px-3 py-2 text-left font-normal">#</th>
                  <th className="px-3 py-2 text-left font-normal">Call</th>
                  <th className="px-3 py-2 text-left font-normal">From</th>
                  <th className="px-3 py-2 text-left font-normal">To</th>
                  <th className="px-3 py-2 text-right font-normal">Life forms</th>
                  <th className="px-3 py-2 text-right font-normal">Weight</th>
                  <th className="px-3 py-2 text-left font-normal">Outcome</th>
                  <th className="px-3 py-2 text-left font-normal">Shuttle</th>
                  <th className="px-3 py-2 text-left font-normal">Requested</th>
                  <th className="px-3 py-2 text-left font-normal">Recorded</th>
                  <th className="px-3 py-2 text-left font-normal">Reason</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row) => (
                  <tr key={row.id} className="border-b border-edge/40 last:border-0">
                    <td className="tnum px-3 py-2 text-muted">{row.id}</td>
                    <td className="px-3 py-2">
                      <Link
                        to="/requests/$id"
                        params={{ id: String(row.travelRequestId) }}
                        className="text-accent hover:underline"
                      >
                        #{row.travelRequestId}
                      </Link>
                    </td>
                    <td className="px-3 py-2">{row.origin}</td>
                    <td className="px-3 py-2">{row.destination}</td>
                    <td className="tnum px-3 py-2 text-right">{row.lifeFormCount}</td>
                    <td className="tnum px-3 py-2 text-right">{row.totalWeightKg} kg</td>
                    <td className="px-3 py-2">
                      <StatusBadge status={row.outcome} size="sm" />
                    </td>
                    {/* null for a rejected call — nothing ever carried it. */}
                    <td className="px-3 py-2">
                      {row.shuttleId === null ? (
                        <span className="text-muted">—</span>
                      ) : (
                        `Shuttle ${row.shuttleId}`
                      )}
                    </td>
                    <td className="px-3 py-2 whitespace-nowrap text-muted">
                      {formatUtc(row.requestedAtUtc)}
                    </td>
                    <td className="px-3 py-2 whitespace-nowrap text-muted">
                      {formatUtc(row.recordedAtUtc)}
                    </td>
                    <td className="max-w-[16rem] truncate px-3 py-2 text-muted">
                      {row.rejectionReason ? (
                        <span title={row.rejectionReason}>{row.rejectionReason}</span>
                      ) : (
                        '—'
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pager
            page={page}
            pageSize={pageSize}
            totalCount={totalCount}
            onPageChange={goToPage}
            onPageSizeChange={changePageSize}
          />

          <p className="text-xs text-muted">
            Ordering is fixed server-side (newest first). This endpoint has no sort,
            filter, or search parameters.
          </p>
        </>
      )}
    </div>
  );
}
