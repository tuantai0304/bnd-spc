import { useState } from 'react';
import { ApiError } from '../api/client';

interface ErrorBannerProps {
  error: ApiError;
  /**
   * A poll that failed *after* a good load is an interruption, not a failure:
   * the page keeps its stale data and says so quietly.
   */
  variant?: 'error' | 'warning';
  dismissible?: boolean;
  onRetry?: () => void;
}

export function ErrorBanner({
  error,
  variant = 'error',
  dismissible = false,
  onRetry,
}: ErrorBannerProps) {
  const [dismissed, setDismissed] = useState(false);
  if (dismissed) return null;

  const warn = variant === 'warning';
  // A 500 deliberately leaks nothing, so show the generic line plus the
  // traceId that support would actually need.
  const message = error.status === 500 ? 'An unexpected error occurred.' : error.message;

  return (
    <div
      role="alert"
      className={`flex flex-wrap items-start gap-3 rounded-lg border px-4 py-3 text-sm ${
        warn
          ? 'border-queued/40 bg-queued/10 text-queued'
          : 'border-rejected/40 bg-rejected/10 text-rejected'
      }`}
    >
      <div className="min-w-0 flex-1">
        <p>{message}</p>
        {error.traceId && (
          <p className="mt-1 text-xs opacity-70">Trace {error.traceId}</p>
        )}
      </div>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="rounded border border-current px-2 py-1 text-xs hover:bg-white/10"
        >
          Retry
        </button>
      )}
      {dismissible && (
        <button
          type="button"
          onClick={() => setDismissed(true)}
          aria-label="Dismiss"
          className="rounded px-2 py-1 text-xs hover:bg-white/10"
        >
          Dismiss
        </button>
      )}
    </div>
  );
}
