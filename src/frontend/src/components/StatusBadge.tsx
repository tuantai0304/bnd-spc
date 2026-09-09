import type { ShuttleState, TravelRequestStatus } from '../api/types';

type Status = ShuttleState | TravelRequestStatus | 'Completed' | 'Rejected';

// Every status maps to a theme token, so a badge can never disagree with the
// progress bar or border that shares its colour.
const TONE: Record<Status, string> = {
  Idle: 'border-idle/40 bg-idle/15 text-idle',
  EnRoute: 'border-enroute/40 bg-enroute/15 text-enroute',
  Arrived: 'border-arrived/40 bg-arrived/15 text-arrived',
  Queued: 'border-queued/40 bg-queued/15 text-queued',
  Assigned: 'border-assigned/40 bg-assigned/15 text-assigned',
  InTransit: 'border-enroute/40 bg-enroute/15 text-enroute',
  Completed: 'border-arrived/40 bg-arrived/15 text-arrived',
  Rejected: 'border-rejected/40 bg-rejected/15 text-rejected',
};

/** `EnRoute` and `InTransit` are one word on the wire but two to a reader. */
const LABEL: Partial<Record<Status, string>> = {
  EnRoute: 'En route',
  InTransit: 'In transit',
};

export function StatusBadge({ status, size = 'md' }: { status: Status; size?: 'sm' | 'md' }) {
  return (
    <span
      className={`inline-flex items-center rounded-full border font-medium whitespace-nowrap ${
        size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-sm'
      } ${TONE[status]}`}
    >
      {LABEL[status] ?? status}
    </span>
  );
}
