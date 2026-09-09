import type { components } from './schema';

type S = components['schemas'];

// The generated types are faithful to the OpenAPI document, but the document
// is looser than the API in three specific ways. This file is the correction;
// components and stores import from here, never from './schema' directly.

// ---- 1. Enums arrive as bare `string` -------------------------------------
// No DTO exposes a C# enum — every slice's mapping calls .ToString() — so the
// generator only ever sees `string`. These are the complete closed sets.
export type ShuttleState = 'Idle' | 'EnRoute' | 'Arrived';

export type TravelRequestStatus =
  | 'Queued'
  | 'Assigned'
  | 'InTransit'
  | 'Completed'
  | 'Rejected';

/** What the dispatcher decided. Branch on this, never on the HTTP status. */
export type DispatchOutcome = 'Assigned' | 'Queued' | 'Rejected';

/** Only terminal calls reach history, so only two outcomes are reachable there. */
export type HistoryOutcome = 'Completed' | 'Rejected';

/** The two statuses after which nothing can change — stop polling on these. */
const TERMINAL: readonly TravelRequestStatus[] = ['Completed', 'Rejected'];

export const isTerminal = (status: TravelRequestStatus): boolean =>
  TERMINAL.includes(status);

// ---- 2. "Optional" fields are always present, just sometimes null ----------
// The server sets no JsonIgnoreCondition, so every property is emitted on the
// wire. The generator marks the non-`required` ones `?:`; they are really
// `| null`, and treating them as possibly-absent forces pointless `?? null`.
//
// `-?` strips both the optional marker and `undefined` from the value, which
// leaves the declared `| null` intact and — unlike adding `| null` outright —
// does not make already-required fields such as `id` or `lifeFormsAboard`
// nullable. They are never null.
type AlwaysPresent<T> = { [K in keyof T]-?: T[K] };

export type Planet = S['PlanetResponse'];

export type ManifestEntry = Omit<S['ManifestEntryResponse'], 'status'> & {
  status: TravelRequestStatus;
};

export type Shuttle = AlwaysPresent<Omit<S['ShuttleResponse'], 'state' | 'manifest'>> & {
  state: ShuttleState;
  manifest: ManifestEntry[];
};

export type LifeForm = S['LifeFormResponse'];

export type CallShuttleBody = S['CallShuttleRequest'];

export type CallShuttleResult = AlwaysPresent<
  Omit<S['CallShuttleResponse'], 'status' | 'outcome'>
> & {
  status: TravelRequestStatus;
  outcome: DispatchOutcome;
};

export type TravelRequest = AlwaysPresent<
  Omit<S['TravelRequestResponse'], 'status' | 'lifeForms'>
> & {
  status: TravelRequestStatus;
  lifeForms: LifeForm[];
};

export type HistoryItem = Omit<S['TravelHistoryItemResponse'], 'outcome'> & {
  outcome: HistoryOutcome;
};

export type HistoryPage = Omit<S['TravelHistoryPageResponse'], 'items'> & {
  items: HistoryItem[];
};

export type PlanetStats = S['PlanetTravelStatsResponse'];

// ---- 3. `format: date-time` is a lie --------------------------------------
// A real captured value is "2026-09-09T04:46:37.7644465" — no Z, no offset,
// because a DateTime round-tripped through SQLite comes back Kind=Unspecified.
// Every *Utc field is an opaque string that must go through parseUtc().
