import { apiFetch } from './client';
import type {
  CallShuttleBody,
  CallShuttleResult,
  HistoryPage,
  Planet,
  PlanetStats,
  Shuttle,
  TravelRequest,
} from './types';

// One typed function per endpoint. Components call stores; stores call these.
// Paths are relative so the Vite dev proxy can keep everything same-origin.

/** The 5 space docks, ordered nearest first. Immutable seed data. */
export const getPlanets = () => apiFetch<Planet[]>('/api/planets');

/** Live fleet state and manifests. A bare array, not an envelope. */
export const getShuttles = () => apiFetch<Shuttle[]>('/api/shuttles');

/**
 * The only write in the API. Returns 201 for all three outcomes — including a
 * party no shuttle could ever carry, which comes back with
 * outcome: 'Rejected'. Branch on `outcome`, never on the HTTP status.
 */
export const callShuttle = (body: CallShuttleBody) =>
  apiFetch<CallShuttleResult>('/api/travel-requests', {
    method: 'POST',
    body: JSON.stringify(body),
  });

/** Poll one call. 404 carries the message in `detail`, not `title`. */
export const getTravelRequest = (id: number) =>
  apiFetch<TravelRequest>(`/api/travel-requests/${id}`);

/**
 * The terminal-calls log, newest first. Ordering is fixed server-side
 * (recordedAtUtc DESC, id DESC); there are no sort, filter, or search params.
 * The OpenAPI document names these Page/PageSize, but model binding is
 * case-insensitive and the repo's .http files use lowercase — so do we.
 */
export const getTravelHistory = (page: number, pageSize: number) =>
  apiFetch<HistoryPage>(
    `/api/travel-history?page=${page}&pageSize=${pageSize}`,
  );

/** Trips per destination planet. Every planet appears, zeroes included. */
export const getPlanetStats = () =>
  apiFetch<PlanetStats[]>('/api/travel-history/stats');
