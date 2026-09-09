import { useEffect } from 'react';
import { Link, createFileRoute } from '@tanstack/react-router';
import { Countdown } from '../components/Countdown';
import { ErrorBanner } from '../components/ErrorBanner';
import { StatusBadge } from '../components/StatusBadge';
import { formatUtc } from '../lib/dates';
import { useTravelRequestStore } from '../stores/useTravelRequestStore';
import type { TravelRequest, TravelRequestStatus } from '../api/types';

export const Route = createFileRoute('/requests/$id')({ component: RequestDetail });

/** The happy path. Rejected is a terminal branch off it, not a sixth node. */
const TIMELINE: TravelRequestStatus[] = ['Queued', 'Assigned', 'InTransit', 'Completed'];

const TIMELINE_LABEL: Record<string, string> = {
  Queued: 'Queued',
  Assigned: 'Assigned',
  InTransit: 'In transit',
  Completed: 'Completed',
};

function NotFound({ message }: { message: string }) {
  return (
    <div className="py-20 text-center">
      <p className="text-lg font-medium">{message}</p>
      <Link to="/" className="mt-3 inline-block text-sm text-accent hover:underline">
        ← Back to the fleet
      </Link>
    </div>
  );
}

function RequestDetail() {
  const { id: rawId } = Route.useParams();
  const { id, data, loading, error, load, refresh, startPolling, stopPolling } =
    useTravelRequestStore();

  // The route is constrained to :int server-side, so a non-integer path does
  // not match any endpoint and comes back as a framework 404 with no JSON body
  // at all. Guard here rather than asking a question the API cannot answer.
  const parsed = /^\d+$/.test(rawId) ? Number(rawId) : null;

  useEffect(() => {
    if (parsed === null) return;
    load(parsed);
    startPolling();
    return stopPolling;
  }, [parsed, load, startPolling, stopPolling]);

  if (parsed === null) return <NotFound message={`No call with id ${rawId} exists.`} />;

  // A 404 on a detail route is the page, not a banner on top of one.
  if (error?.status === 404) return <NotFound message={error.message} />;

  // Wait for the store to point at this id before trusting its data, or a
  // fast navigation briefly renders the previous call under the new heading.
  const request = id === parsed ? data : null;

  if (!request) {
    if (error) {
      return (
        <div className="py-16">
          <ErrorBanner error={error} onRetry={() => void refresh()} />
        </div>
      );
    }
    return <p className="py-20 text-center text-muted">{loading ? 'Loading call…' : ' '}</p>;
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold tracking-tight">Call #{request.id}</h1>
        <StatusBadge status={request.status} />
      </header>

      {/* A poll that fails after a good load must never blank the page. */}
      {error && <ErrorBanner error={error} variant="warning" dismissible />}

      <Timeline status={request.status} />

      {request.rejectionReason && (
        // The reason already names the fleet's ceilings, so it needs no gloss.
        <div className="rounded-lg border border-queued/40 bg-queued/10 p-4 text-sm">
          <p className="font-medium text-queued">No shuttle could carry this party</p>
          <p className="mt-1 text-ink">{request.rejectionReason}</p>
        </div>
      )}

      {request.status === 'Queued' && (
        <p className="rounded-lg border border-edge bg-panel p-4 text-sm text-muted">
          Waiting for a shuttle to free up. This page updates automatically.
        </p>
      )}

      <Details request={request} />
      <LifeForms request={request} />

      <p className="text-xs text-muted">
        A placed call cannot be edited or cancelled — the API exposes no write for an
        existing call.
      </p>
    </div>
  );
}

function Timeline({ status }: { status: TravelRequestStatus }) {
  const rejected = status === 'Rejected';
  const reachedIndex = rejected ? -1 : TIMELINE.indexOf(status);

  return (
    <ol className="flex flex-wrap items-center gap-2 rounded-xl border border-edge bg-panel p-4 text-sm">
      {TIMELINE.map((node, i) => {
        // Queued and Assigned both mean "not yet flying"; only InTransit has an ETA.
        const reached = !rejected && i <= reachedIndex;
        const current = !rejected && i === reachedIndex;
        return (
          <li key={node} className="flex items-center gap-2">
            <span
              className={`rounded-full px-3 py-1 ${
                current
                  ? 'bg-accent/20 font-medium text-accent ring-1 ring-accent/50'
                  : reached
                    ? 'text-ink'
                    : 'text-muted/50'
              }`}
            >
              {TIMELINE_LABEL[node]}
            </span>
            {i < TIMELINE.length - 1 && <span className="text-edge">→</span>}
          </li>
        );
      })}
      <li className="flex items-center gap-2">
        <span className="text-edge">↘</span>
        <span
          className={`rounded-full px-3 py-1 ${
            rejected
              ? 'bg-rejected/20 font-medium text-rejected ring-1 ring-rejected/50'
              : 'text-muted/50'
          }`}
        >
          Rejected
        </span>
      </li>
    </ol>
  );
}

function Details({ request }: { request: TravelRequest }) {
  const rows: Array<[string, React.ReactNode]> = [
    ['From', request.origin],
    ['To', request.destination],
    ['Party size', request.lifeFormCount],
    ['Total weight', `${request.totalWeightKg} kg`],
    [
      'Shuttle',
      request.shuttleName ? (
        <Link to="/" className="text-accent hover:underline">
          {request.shuttleName}
        </Link>
      ) : (
        <span className="text-muted">Not yet assigned</span>
      ),
    ],
    ['Requested', formatUtc(request.requestedAtUtc)],
    ['Completed', formatUtc(request.completedAtUtc)],
  ];

  // estimatedArrivalUtc and secondsUntilArrival are populated only while
  // InTransit, so an ETA row on any other status would render an em dash.
  if (request.status === 'InTransit' && request.estimatedArrivalUtc) {
    rows.push([
      'ETA',
      <span key="eta">
        {formatUtc(request.estimatedArrivalUtc)}
        {request.secondsUntilArrival !== null && (
          <span className="ml-2 text-enroute">
            (<Countdown seconds={request.secondsUntilArrival} />)
          </span>
        )}
      </span>,
    ]);
  }

  return (
    <dl className="grid gap-x-6 gap-y-3 rounded-xl border border-edge bg-panel p-4 text-sm sm:grid-cols-2">
      {rows.map(([label, value]) => (
        <div key={label} className="flex justify-between gap-4">
          <dt className="text-muted">{label}</dt>
          <dd className="text-right">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

function LifeForms({ request }: { request: TravelRequest }) {
  return (
    <section className="overflow-hidden rounded-xl border border-edge bg-panel">
      <table className="w-full text-sm">
        <caption className="border-b border-edge px-4 py-3 text-left font-medium">
          Life forms
        </caption>
        <thead className="text-muted">
          <tr>
            <th className="px-4 py-2 text-left font-normal">Species</th>
            <th className="px-4 py-2 text-right font-normal">Weight (kg)</th>
          </tr>
        </thead>
        <tbody>
          {request.lifeForms.map((lf, i) => (
            <tr key={`${lf.species}-${i}`} className="border-t border-edge/50">
              <td className="px-4 py-2">{lf.species}</td>
              <td className="tnum px-4 py-2 text-right">{lf.weightKg}</td>
            </tr>
          ))}
        </tbody>
        <tfoot className="border-t border-edge text-muted">
          <tr>
            <td className="px-4 py-2">{request.lifeFormCount} total</td>
            <td className="tnum px-4 py-2 text-right">{request.totalWeightKg}</td>
          </tr>
        </tfoot>
      </table>
    </section>
  );
}
