/**
 * Parse a backend *Utc value. Always treat it as UTC, suffix or not.
 *
 * Every *Utc field is a C# DateTime, not a DateTimeOffset. A value that has
 * been round-tripped through SQLite comes back Kind=Unspecified and serializes
 * *without* a Z; one still in memory serializes *with* one. So the same field
 * is sometimes "...7644465" and sometimes "...7644465Z", and
 * new Date("2026-09-09T04:46:37.7644465") is read as local time — silently
 * wrong by your UTC offset. There is no acceptable exception to this helper.
 */
export function parseUtc(value: string): Date {
  const hasZone = /[Zz]$|[+-]\d{2}:?\d{2}$/.test(value);
  return new Date(hasZone ? value : `${value}Z`);
}

/** Absolute local rendering of a backend timestamp. `—` for a null field. */
export const formatUtc = (value: string | null | undefined): string =>
  value ? parseUtc(value).toLocaleString() : '—';

/** Server-supplied countdowns are already floored at 0 by the API. */
export const formatSeconds = (s: number): string =>
  s <= 0 ? 'arriving' : `${Math.ceil(s)}s`;
