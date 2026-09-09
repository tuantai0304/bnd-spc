import { useEffect, useState } from 'react';
import { formatSeconds } from '../lib/dates';

/**
 * Ticks down locally between polls so an arrival reads smoothly at 4 Hz rather
 * than lurching once a second, and re-syncs whenever the server sends a new
 * value — the server, not this component, is the authority on the remaining
 * time. The effect re-runs on every new `seconds` prop, which is the re-sync.
 */
export function Countdown({ seconds }: { seconds: number }) {
  const [remaining, setRemaining] = useState(seconds);

  useEffect(() => {
    setRemaining(seconds);
    if (seconds <= 0) return;

    const startedAt = Date.now();
    const id = setInterval(() => {
      setRemaining(Math.max(0, seconds - (Date.now() - startedAt) / 1000));
    }, 250);
    return () => clearInterval(id);
  }, [seconds]);

  return <span className="tnum">{formatSeconds(remaining)}</span>;
}
