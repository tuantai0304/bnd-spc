# Frontend Product Design Requirements — Space Travel SPA

**Source documents:** [`backend-implementation-spec.md`](backend-implementation-spec.md),
[`brd-analysis.md`](brd-analysis.md), [`assumptions.md`](assumptions.md),
[`how-to-run.md`](how-to-run.md), and the implemented backend at
[`src/backend/SpaceTravel.Api/`](../src/backend/SpaceTravel.Api/).

**Purpose of this document:** specify the frontend SPA completely enough to implement
without reading a line of C#. Every endpoint is documented with a **real, captured**
request and response — not an invented example — so there is zero guessing about the API
contract.

**Conventions.** Items marked **[PDR decision]** are frontend choices made while writing
this document, not requirements imposed by the backend; they may be changed freely.
Everything else describes behaviour the backend *actually has* and cannot be changed
without editing the API. Blocks marked **⚠ Gotcha** are places where the obvious
assumption is wrong — read those even if you skim the rest.

**Verification status.** Every JSON payload below was captured from a running instance on
2026-09-09 (`dotnet run`, seeded database). Every validation message was copied verbatim
from its `AbstractValidator`.

---

## 0. Purpose & Scope

The backend implements the BRD in full; this document covers the UI on top of it — the
brief's *"UI to illustrate how the passenger pickup and shuttle system works"*.

**In scope:** five pages (§5), the paging contract (§6), error handling (§7).

**Out of scope, because the backend has none of it:** authentication, authorization, users,
i18n, SSR, offline support, file upload, real-time push.

### 0.1 The API in one table

| # | Method | Route | Purpose | Paged | Validated |
|---|---|---|---|---|---|
| 1 | GET | `/health` | Liveness probe | — | — |
| 2 | GET | `/api/planets` | The 5 space docks, nearest first | No | — |
| 3 | GET | `/api/shuttles` | Live fleet state + manifests | No | — |
| 4 | POST | `/api/travel-requests` | **Call a shuttle** (the only write) | — | Yes |
| 5 | GET | `/api/travel-requests/{id}` | Poll one call | No | — |
| 6 | GET | `/api/travel-history` | Completed + rejected log | **Yes** | Yes |
| 7 | GET | `/api/travel-history/stats` | Trips per destination planet | No | — |

**Base URL:** `http://localhost:5095` — HTTP only. This is the sole launch profile in
[`Properties/launchSettings.json`](../src/backend/SpaceTravel.Api/Properties/launchSettings.json);
there is no HTTPS profile.

### 0.2 The five things that will bite you

Read these before writing any code. Each is expanded later with evidence.

1. **A rejected trip returns `201 Created`, not an error.** Branch on the `outcome` field,
   never on HTTP status. (§5.1)
2. **Response payloads are camelCase, but validation error *keys* are PascalCase.**
   `{"errors": {"DestinationPlanetId": [...]}}` while the field you posted was
   `destinationPlanetId`. You need a mapping layer. (§4.2)
3. **Timestamps often have no `Z` suffix** even though they are UTC, and the OpenAPI schema
   claims `format: date-time`. Parsing them directly gives local time — wrong by your
   offset. (§4.3)
4. **There is no `totalPages` in the paging envelope.** Derive it. (§6)
5. **The world changes on its own every 500 ms** and there is no WebSocket. You must
   poll. (§4.4)

---

## 1. Stack & Setup

| Concern | Choice |
|---|---|
| Build | Vite |
| UI | React 19 + TypeScript (`strict: true`) |
| Routing | TanStack Router (file-based) |
| State | Zustand |
| Styling | Tailwind CSS **v4** |
| Types | `openapi-typescript` against the backend's OpenAPI document |

```bash
npm create vite@latest frontend -- --template react-ts
cd frontend
npm i @tanstack/react-router zustand
npm i -D @tanstack/router-plugin tailwindcss @tailwindcss/vite openapi-typescript
```

### 1.1 Tailwind v4 — not v3

Tailwind v4 is configured **in CSS**, not in a JS config file. Do not create
`tailwind.config.js`, and do not add `postcss.config.js` — the Vite plugin handles it.

`src/index.css`:

```css
@import "tailwindcss";

@theme {
  --color-idle: oklch(0.65 0.02 250);
  --color-enroute: oklch(0.72 0.16 250);
  --color-arrived: oklch(0.75 0.15 150);
  --color-queued: oklch(0.78 0.13 90);
  --color-rejected: oklch(0.65 0.20 25);
}
```

### 1.2 `vite.config.ts`

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { TanStackRouterVite } from '@tanstack/router-plugin/vite';

export default defineConfig({
  plugins: [TanStackRouterVite(), react(), tailwindcss()],
  server: {
    proxy: {
      // See section 2 - this is what makes the app work without backend CORS.
      '/api': { target: 'http://localhost:5095', changeOrigin: true },
      '/openapi': { target: 'http://localhost:5095', changeOrigin: true },
    },
  },
});
```

---

## 2. Dev Environment & the CORS Problem

> ⚠ **Gotcha — the backend has no CORS configuration at all.**
> [`Program.cs`](../src/backend/SpaceTravel.Api/Program.cs) calls neither `AddCors()` nor
> `UseCors()`. A browser on `http://localhost:5173` calling `http://localhost:5095`
> directly is blocked by the same-origin policy, and the failure is opaque — a network
> error with no useful message.

**[PDR decision]** Solve this in the frontend with the Vite proxy from §1.2 rather than
changing the backend. Consequences:

- **All fetches use relative paths** — `fetch('/api/shuttles')`, never
  `fetch('http://localhost:5095/api/shuttles')`.
- No `VITE_API_BASE_URL` variable is needed in development.
- Same-origin in the browser, so no preflight and no cookie/credential complications.

### 2.1 Production

The proxy is a dev-server feature and does not exist in a production build. Deploying the
SPA on a different origin from the API requires **backend** work not covered by this
document:

```csharp
// Program.cs - NOT currently present.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("https://your-spa-origin.example")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// after app.UseExceptionHandler():
app.UseCors();
```

Alternatively, serve the built SPA from the API's own `wwwroot` so the origins match and no
CORS is needed. **[PDR decision]** Prefer this for the challenge — it keeps *"running the
server should be easy"* true.

### 2.2 Running both

```bash
# terminal 1
cd src/backend/SpaceTravel.Api && dotnet run     # http://localhost:5095

# terminal 2
cd frontend && npm run dev                       # http://localhost:5173
```

The API migrates and seeds SQLite on startup, so there is no separate setup step.

---

## 3. Folder Structure

**[PDR decision]**

```
frontend/src/
├── api/
│   ├── schema.d.ts          # GENERATED - never edit by hand
│   ├── types.ts             # narrowing layer over the generated types (4.1)
│   ├── client.ts            # apiFetch + ApiError (4.2)
│   └── endpoints.ts         # one typed function per endpoint
├── stores/
│   ├── usePlanetsStore.ts
│   ├── useFleetStore.ts
│   ├── useTravelRequestStore.ts
│   └── useHistoryStore.ts
├── components/
│   ├── StatusBadge.tsx      ├── Pager.tsx
│   ├── CapacityBar.tsx      ├── ErrorBanner.tsx
│   └── FieldError.tsx       └── Countdown.tsx
├── lib/
│   ├── dates.ts             # parseUtc (4.3)
│   └── polling.ts           # shared interval helper (4.4)
├── routes/                  # TanStack Router file-based routes (4.5)
│   ├── __root.tsx           ├── index.tsx          (Fleet Dashboard)
│   ├── call.tsx             ├── requests.$id.tsx
│   ├── history.tsx          └── stats.tsx
└── index.css
```

---

## 4. Shared Foundations

### 4.1 Type generation

The backend serves an OpenAPI 3.1.1 document at **`/openapi/v1.json`** (Development only).
Generate types from it rather than hand-transcribing them.

```jsonc
// package.json
"scripts": {
  "gen:api": "openapi-typescript http://localhost:5095/openapi/v1.json -o src/api/schema.d.ts"
}
```

Run `npm run gen:api` **while the backend is running**, and re-run it after any backend
change.

**[PDR decision] Why `openapi-typescript` and not `orval` / `kubb`:** those generate
TanStack Query hooks, which would own fetching and caching. This app's fetching is owned by
Zustand stores with explicit polling lifecycles (§4.4), so a hook generator would fight the
architecture. `openapi-typescript` emits types only — no runtime — which is exactly the
part worth generating.

#### The narrowing layer — `src/api/types.ts`

The generated types are correct but too loose in three specific ways. This file is the
hand-written correction and **is not optional**.

```ts
import type { components } from './schema';

type S = components['schemas'];

// ---- 1. Enums arrive as bare `string` -------------------------------------
// No DTO exposes a C# enum; each slice's mapping calls .ToString(), so the
// generator only ever sees `string`. These are the complete closed sets.
export type ShuttleState = 'Idle' | 'EnRoute' | 'Arrived';
export type TravelRequestStatus =
  | 'Queued' | 'Assigned' | 'InTransit' | 'Completed' | 'Rejected';
export type DispatchOutcome = 'Assigned' | 'Queued' | 'Rejected';

// ---- 2. "Optional" fields are always present, just sometimes null ----------
// The server does not set JsonIgnoreCondition, so every property is emitted.
// The generator marks non-`required` ones `?:`; they are really `| null`.
type AlwaysPresent<T> = { [K in keyof Required<T>]: T[K] | null };

export type Planet            = S['PlanetResponse'];
export type ManifestEntry     = Omit<S['ManifestEntryResponse'], 'status'>
                              & { status: TravelRequestStatus };
export type Shuttle           = AlwaysPresent<Omit<S['ShuttleResponse'], 'state' | 'manifest'>>
                              & { state: ShuttleState; manifest: ManifestEntry[] };
export type LifeForm          = S['LifeFormResponse'];
export type CallShuttleBody   = S['CallShuttleRequest'];
export type CallShuttleResult = AlwaysPresent<Omit<S['CallShuttleResponse'], 'status' | 'outcome'>>
                              & { status: TravelRequestStatus; outcome: DispatchOutcome };
export type TravelRequest     = AlwaysPresent<Omit<S['TravelRequestResponse'], 'status' | 'lifeForms'>>
                              & { status: TravelRequestStatus; lifeForms: LifeForm[] };
export type HistoryItem       = Omit<S['TravelHistoryItemResponse'], 'outcome'>
                              & { outcome: 'Completed' | 'Rejected' };
export type HistoryPage       = S['TravelHistoryPageResponse'];
export type PlanetStats       = S['PlanetTravelStatsResponse'];
```

> ⚠ **Gotcha — the schema's `format: date-time` is a lie.** A real captured value is
> `"2026-09-09T04:46:37.7644465"` — no `Z`, no offset. Treat every `*Utc` field as an
> opaque string and run it through `parseUtc()`. See §4.3.

### 4.2 API client — `src/api/client.ts`

The backend returns **three structurally different error envelopes** (§7). One parser must
handle all three and normalize them.

```ts
export type FieldErrors = Record<string, string[]>;

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly fieldErrors: FieldErrors = {},
    readonly traceId?: string,
  ) { super(message); this.name = 'ApiError'; }

  /** True when at least one error can be shown against a form input. */
  get hasFieldErrors() { return Object.keys(this.fieldErrors).length > 0; }
}

/** PascalCase validation keys -> the camelCase names the form actually uses. */
const FIELD_KEY_MAP: Record<string, string> = {
  OriginPlanetId: 'originPlanetId',
  DestinationPlanetId: 'destinationPlanetId',
  LifeForms: 'lifeForms',
  Page: 'page',
  PageSize: 'pageSize',
};

const INDEXED = /^LifeForms\[(\d+)\]\.(Species|WeightKg)$/;

export function normalizeFieldKey(key: string): string {
  const m = INDEXED.exec(key);
  if (m) {
    const [, i, prop] = m;
    return `lifeForms[${i}].${prop.charAt(0).toLowerCase()}${prop.slice(1)}`;
  }
  return FIELD_KEY_MAP[key] ?? key.charAt(0).toLowerCase() + key.slice(1);
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...init?.headers,
      },
    });
  } catch {
    // Network-level failure. With the Vite proxy in place this almost always
    // means the backend is not running.
    throw new ApiError(0, 'Cannot reach the API. Is the backend running on port 5095?');
  }

  const body = await res.json().catch(() => null);

  if (!res.ok) {
    const fieldErrors: FieldErrors = {};
    // Envelope 1: validation problem - the only one carrying `errors`.
    if (body && typeof body.errors === 'object' && body.errors) {
      for (const [k, v] of Object.entries(body.errors as FieldErrors)) {
        fieldErrors[normalizeFieldKey(k)] = v;
      }
    }
    // Envelope 2 uses `detail`; envelopes 1 and 3 put the message in `title`.
    const message = body?.detail || body?.title || `Request failed (${res.status}).`;
    throw new ApiError(res.status, message, fieldErrors, body?.traceId);
  }

  return body as T;
}
```

### 4.3 Dates — `src/lib/dates.ts`

> ⚠ **Gotcha.** Every `*Utc` field is a C# `DateTime`, not a `DateTimeOffset`. Values that
> have been round-tripped through SQLite come back with `Kind=Unspecified` and therefore
> serialize **without** a `Z`. Values still in memory serialize **with** one. So the same
> field is sometimes `"...7644465"` and sometimes `"...7644465Z"` — and
> `new Date("2026-09-09T04:46:37.7644465")` is interpreted as **local** time, silently
> shifting it by your UTC offset.

```ts
/** Parse a backend *Utc value. Always treat it as UTC, suffix or not. */
export function parseUtc(value: string): Date {
  const hasZone = /[Zz]$|[+-]\d{2}:?\d{2}$/.test(value);
  return new Date(hasZone ? value : `${value}Z`);
}

export const formatUtc = (v: string) => parseUtc(v).toLocaleString();

/** Server-supplied countdowns are already floored at 0 by the API. */
export const formatSeconds = (s: number) => (s <= 0 ? 'arriving' : `${Math.ceil(s)}s`);
```

### 4.4 State — Zustand stores

> ⚠ **Gotcha — the world moves without you.** A `SimulationTickService` advances every
> shuttle every **500 ms**. A leg takes `max(1, |rankDifference|) × 3 seconds`. There is no
> SignalR, no WebSocket, and no SSE endpoint. **Polling is the only way to stay current.**
>
> This is not theoretical: while capturing payloads for this document, a call returned
> `"outcome": "Queued"` and, polled a few seconds later, had already become
> `"status": "Assigned"` with a shuttle attached.

**[PDR decision]** Each store owns its own polling lifecycle. Components start polling on
mount and stop on unmount, so no interval outlives its page.

```ts
// src/lib/polling.ts
export function createPoller(fn: () => void | Promise<void>, ms: number) {
  let id: ReturnType<typeof setInterval> | null = null;
  return {
    start() { if (id === null) { void fn(); id = setInterval(() => void fn(), ms); } },
    stop()  { if (id !== null) { clearInterval(id); id = null; } },
  };
}
```

| Store | Data | Poll interval | Stop condition |
|---|---|---|---|
| `usePlanetsStore` | `Planet[]` | **never** — fetch once, cache forever | n/a (seed data is immutable) |
| `useFleetStore` | `Shuttle[]` | 1000 ms | on unmount |
| `useTravelRequestStore` | one `TravelRequest` | 1000 ms | **on unmount, or when `status` is `Completed`/`Rejected`** |
| `useHistoryStore` | `HistoryPage` | none — refetch when page/pageSize changes | n/a |
| *(stats, local to the page)* | `PlanetStats[]` | none — fetch on mount + Refresh button | n/a |

Every store exposes the same shape:

```ts
interface StoreShape<T> {
  data: T | null;
  loading: boolean;      // true only on the FIRST load, so polling never flickers the UI
  error: ApiError | null;
  refresh: () => Promise<void>;
  startPolling: () => void;
  stopPolling: () => void;
}
```

> **[PDR decision] Do not set `loading: true` on poll refreshes.** A spinner every second
> makes the dashboard unreadable. Keep showing stale data and surface poll failures in a
> non-blocking banner.

### 4.5 Routing — TanStack Router

| Route file | Path | Page | Search params |
|---|---|---|---|
| `routes/index.tsx` | `/` | Fleet Dashboard (§5.2) | — |
| `routes/call.tsx` | `/call` | Call a Shuttle (§5.1) | — |
| `routes/requests.$id.tsx` | `/requests/$id` | Request Detail (§5.3) | — |
| `routes/history.tsx` | `/history` | Travel History (§5.4) | `page`, `pageSize` |
| `routes/stats.tsx` | `/stats` | Planet Stats (§5.5) | — |

`/history` validates its search params so the URL is shareable and the back button works:

```ts
export const Route = createFileRoute('/history')({
  validateSearch: (s: Record<string, unknown>) => ({
    page: Math.max(1, Number(s.page ?? 1) || 1),
    pageSize: [10, 25, 50, 100].includes(Number(s.pageSize)) ? Number(s.pageSize) : 25,
  }),
});
```

Clamping here means the UI never sends a value the backend would reject — but §6 still
documents those 400s, because a hand-edited URL can still produce them.

---

## 5. Pages

Every page section follows the same template: **Route · Endpoints · Request · Response ·
UI layout · Actions · Validation · Error handling · Polling.**

### 5.1 Call a Shuttle

The only page in the application that writes anything.

**Route:** `/call`

#### Endpoints

| Purpose | Call |
|---|---|
| Populate the two planet selects | `GET /api/planets` (§5.5 shows the payload) |
| Submit the call | `POST /api/travel-requests` |

#### Request

```
POST /api/travel-requests
Content-Type: application/json
```

```json
{
  "originPlanetId": 1,
  "destinationPlanetId": 2,
  "lifeForms": [
    { "species": "Human", "weightKg": 70 },
    { "species": "Ewok", "weightKg": 25 }
  ]
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `originPlanetId` | `int` | Yes | Must be an existing planet id (1–5) |
| `destinationPlanetId` | `int` | Yes | Must exist **and** differ from origin |
| `lifeForms` | `array` | Yes | At least one entry |
| `lifeForms[].species` | `string` | Yes | Non-empty, max 100 chars |
| `lifeForms[].weightKg` | `decimal` | Yes | Greater than 0 |

#### Responses — all three outcomes are `201 Created`

> ⚠ **Gotcha — the single most important thing on this page.** The endpoint returns
> **`201 Created` even when the dispatcher refused the party.** The call is a real,
> persisted, pollable record either way; the decision lives in the body. Branching on
> `res.ok` will show a rejected party a success screen.
>
> The `Location` header is `/api/travel-requests/{travelRequestId}` in all three cases.

**(a) Assigned** — a shuttle took the party:

```json
{
  "travelRequestId": 2,
  "status": "Assigned",
  "outcome": "Assigned",
  "lifeFormCount": 2,
  "totalWeightKg": 95,
  "shuttleId": 2,
  "shuttleName": "Shuttle 2",
  "queuePosition": null,
  "rejectionReason": null
}
```

**(b) Queued** — every shuttle was busy; the party waits FIFO:

```json
{
  "travelRequestId": 9,
  "status": "Queued",
  "outcome": "Queued",
  "lifeFormCount": 1,
  "totalWeightKg": 105,
  "shuttleId": null,
  "shuttleName": null,
  "queuePosition": 1,
  "rejectionReason": null
}
```

**(c) Rejected** — no shuttle in the fleet could *ever* carry this party:

```json
{
  "travelRequestId": 3,
  "status": "Rejected",
  "outcome": "Rejected",
  "lifeFormCount": 1,
  "totalWeightKg": 5000,
  "shuttleId": null,
  "shuttleName": null,
  "queuePosition": null,
  "rejectionReason": "No shuttle can carry 1 life form(s) weighing 5000kg. The largest shuttle takes 20 life forms or 4000kg."
}
```

Field meanings:

| Field | Type | Populated when |
|---|---|---|
| `travelRequestId` | `int` | Always — use it to navigate to §5.3 |
| `status` | `TravelRequestStatus` | Always |
| `outcome` | `'Assigned' \| 'Queued' \| 'Rejected'` | Always — **branch on this** |
| `lifeFormCount` | `int` | Always |
| `totalWeightKg` | `decimal` | Always |
| `shuttleId` / `shuttleName` | `int?` / `string?` | Only when `outcome === 'Assigned'` |
| `queuePosition` | `int?` | Only when `outcome === 'Queued'` (1-based) |
| `rejectionReason` | `string?` | Only when `outcome === 'Rejected'` |

#### UI layout

**Form inputs**

| Control | Type | Source | Notes |
|---|---|---|---|
| Origin planet | `<select>` | `GET /api/planets` | Label `name`, value `id`, ordered by `distanceRank` |
| Destination planet | `<select>` | `GET /api/planets` | Same; **disable the option equal to origin** |
| Life form rows | repeatable group | user input | Starts with exactly one row |
| — Species | `<input type="text">` | | `maxLength={100}` |
| — Weight (kg) | `<input type="number">` | | `min` above 0, `step="0.1"` |
| — Remove row | button | | Hidden when only one row remains |
| Add life form | button | | Appends an empty row |
| Call a shuttle | submit button | | Disabled while in flight |

**Live summary panel** — recompute on every change, no API call:

- Party size: `lifeForms.length`
- Total weight: `sum(weightKg)`
- Capacity hint: *"Largest shuttle: 20 life forms or 4000 kg"*
- A soft warning when the party exceeds either ceiling: *"No shuttle can carry this party —
  the call will be rejected."*

> **[PDR decision]** That warning is advisory, not blocking. Let the user submit anyway: the
> backend records the rejection in history, and rejections are exactly what the stats page
> is designed to surface (a dock that keeps turning parties away is where the next shuttle
> earns its keep). Blocking submission would hide that signal.

#### Actions

| Action | Trigger | API call | On success |
|---|---|---|---|
| **Create** | Submit | `POST /api/travel-requests` | Navigate to `/requests/{travelRequestId}` |
| Add life form | Add button | none — local state | Row appended |
| Remove life form | Remove button | none — local state | Row removed |

**Update / Delete: not available.** There is no `PUT`, `PATCH`, or `DELETE` on any resource
in this API. A submitted call cannot be edited or cancelled. See §5.6.

#### Validation

Client-side rules mirror the server exactly. Source:
[`CallShuttleValidator.cs`](../src/backend/SpaceTravel.Api/Features/CallShuttle/CallShuttleValidator.cs).
The **Message** column is copied verbatim — display these strings rather than inventing your
own, so the client and server never disagree.

| # | Error key | Condition | Message (verbatim) |
|---|---|---|---|
| 1 | `OriginPlanetId` | Not an existing planet id | `Origin must be one of the known planets.` |
| 2 | `DestinationPlanetId` | Not an existing planet id | `Destination must be one of the known planets.` |
| 3 | `DestinationPlanetId` | Equals `originPlanetId` | `Destination must differ from the planet you are calling from.` |
| 4 | `LifeForms` | `null` | `'Life Forms' must not be empty.` |
| 5 | `LifeForms` | `null` or empty array | `A call must include at least one life form.` |
| 6 | `LifeForms[N].Species` | Empty or whitespace | `'Species' must not be empty.` |
| 7 | `LifeForms[N].Species` | Longer than 100 chars | `Every life form needs a species.` |
| 8 | `LifeForms[N].WeightKg` | `<= 0` | `Every life form must weigh more than zero kilograms.` |

> ⚠ **Gotcha — rules 4 and 6 have surprising messages.** FluentValidation's `.WithMessage()`
> binds only to the rule immediately preceding it. In the validator, `.WithMessage()` follows
> `.Must(...)` in rule 5 and `.MaximumLength(100)` in rule 7, so `.NotNull()` and
> `.NotEmpty()` fall back to **default** framework messages. That is why an empty species
> says `'Species' must not be empty.` and not the author's intended
> `Every life form needs a species.` — the latter appears only when the species is *too
> long*. This is backend behaviour; the table above is what the API actually returns.

> ⚠ **Gotcha — capacity is deliberately NOT validated.** Nothing rejects an over-capacity
> party at the validation layer. Whether a shuttle can carry the party is a *business*
> decision made by the `Fleet` aggregate, which is why it comes back as `201` +
> `outcome: "Rejected"` rather than `400`. Do not add a client-side rule that blocks it.

A validation failure returns `400` with **every** broken rule at once:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "DestinationPlanetId": ["Destination must be one of the known planets."],
    "LifeForms[0].Species": ["'Species' must not be empty."],
    "LifeForms[0].WeightKg": ["Every life form must weigh more than zero kilograms."]
  },
  "traceId": "00-81cd1822284734a47b9d5158838de5b9-7379b129c68ef00b-00"
}
```

Two rules can fire on the same field simultaneously:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "DestinationPlanetId": [
      "Destination must be one of the known planets.",
      "Destination must differ from the planet you are calling from."
    ]
  },
  "traceId": "00-67d430aa59d0b94f641c672edf8aa15e-e5c068261e98a7d7-00"
}
```

#### Error handling

| Case | Display |
|---|---|
| `400` with `errors` | Inline, red, under the matching input. Map `LifeForms[0].WeightKg` → the weight input of row 0 via `normalizeFieldKey` (§4.2). Do **not** show a toast as well. |
| `201` + `outcome: "Rejected"` | **Not an error.** Amber result panel with `rejectionReason` verbatim, plus a link to the request detail page. |
| `409` | Banner with `title`. Rare — a domain state-machine violation. |
| `500` | Banner: *"An unexpected error occurred."* Include `traceId` in small print. |
| Network failure | Banner: *"Cannot reach the API. Is the backend running on port 5095?"* |

Clear all field errors on the next submit so stale messages never linger.

#### Polling

None. This page is a form.

---

### 5.2 Fleet Dashboard

The brief's core deliverable: *"UI to illustrate how the passenger pickup and shuttle system
works."*

**Route:** `/` (home)

#### Endpoint

```
GET /api/shuttles
Accept: application/json
```

No parameters. No request body.

#### Response — `200 OK`

A **bare JSON array**, not an envelope. Captured with one shuttle in flight:

```json
[
  {
    "id": 1,
    "name": "Shuttle 1",
    "state": "Idle",
    "currentPlanet": "Boreth",
    "flyingToPlanet": null,
    "tripDestination": null,
    "arrivesAtUtc": null,
    "secondsUntilArrival": null,
    "lifeFormsAboard": 0,
    "weightAboardKg": 0,
    "remainingLifeForms": 20,
    "remainingWeightKg": 4000,
    "manifest": []
  },
  {
    "id": 3,
    "name": "Shuttle 3",
    "state": "EnRoute",
    "currentPlanet": null,
    "flyingToPlanet": "Argus X",
    "tripDestination": "Argus X",
    "arrivesAtUtc": "2026-09-09T04:47:21.7763202",
    "secondsUntilArrival": 7.9466113,
    "lifeFormsAboard": 2,
    "weightAboardKg": 278.5,
    "remainingLifeForms": 18,
    "remainingWeightKg": 3721.5,
    "manifest": [
      {
        "travelRequestId": 4,
        "status": "InTransit",
        "lifeFormCount": 2,
        "totalWeightKg": 278.5
      }
    ]
  }
]
```

| Field | Type | Notes |
|---|---|---|
| `id` | `int` | |
| `name` | `string` | e.g. `"Shuttle 3"` |
| `state` | `'Idle' \| 'EnRoute' \| 'Arrived'` | Exactly three states |
| `currentPlanet` | `string \| null` | Planet **name**. `null` while `EnRoute` |
| `flyingToPlanet` | `string \| null` | Planet **name**. Non-null **only** while `EnRoute` |
| `tripDestination` | `string \| null` | Where the manifest is going. `null` when empty |
| `arrivesAtUtc` | `string \| null` | Non-null only while `EnRoute`. **Run through `parseUtc`** |
| `secondsUntilArrival` | `number \| null` | Already floored at 0 by the server |
| `lifeFormsAboard` | `int` | Sum over the manifest |
| `weightAboardKg` | `decimal` | Sum over the manifest |
| `remainingLifeForms` | `int` | `20 - lifeFormsAboard` |
| `remainingWeightKg` | `decimal` | `4000 - weightAboardKg` |
| `manifest` | `ManifestEntry[]` | Empty array when idle, never `null` |

> **Note on `Arrived`.** It is a transient state that exists for a single simulation tick
> before settling to `Idle`. You will rarely observe it; render it, but do not build logic
> that depends on catching it.

> **Note on `flyingToPlanet` vs `tripDestination`.** They differ during a *repositioning
> leg*: when a shuttle flies empty to collect a party, `flyingToPlanet` is the pickup dock
> while `tripDestination` is the party's final destination. Showing both is what makes the
> "smarts" visible in the UI.

#### UI layout

A responsive grid of one card per shuttle (4 cards, from seed data).

**Card contents**

| Element | Source | Rendering |
|---|---|---|
| Title | `name` | |
| State badge | `state` | `Idle` grey · `EnRoute` blue · `Arrived` green |
| Location line | `state`-dependent | Idle/Arrived: *"At {currentPlanet}"* · EnRoute: *"{currentPlanet ?? '—'} → {flyingToPlanet}"* |
| Trip line | `tripDestination` | *"Carrying to {tripDestination}"*, hidden when `null` |
| Countdown | `secondsUntilArrival` | *"Arrives in 8s"*. Tick down locally between polls for smoothness; re-sync on each poll |
| Life form capacity | `lifeFormsAboard` / 20 | Progress bar + `"2 / 20"` |
| Weight capacity | `weightAboardKg` / 4000 | Progress bar + `"278.5 / 4000 kg"` |
| Manifest table | `manifest` | Hidden when empty |

**Manifest sub-table columns**

| Column | Field |
|---|---|
| Request | `travelRequestId` — links to `/requests/{id}` |
| Status | `status` badge |
| Life forms | `lifeFormCount` |
| Weight | `totalWeightKg` + `" kg"` |

**Page header:** fleet totals (shuttles idle / en route, total life forms aboard) and a
"last updated" timestamp.

> **[PDR decision]** Derive capacity ceilings as
> `lifeFormsAboard + remainingLifeForms` rather than hardcoding 20 / 4000. The values are
> configuration (`Fleet:MaxLifeFormsPerShuttle`) and could change.

#### Actions

**None. This page is entirely read-only.** There is no endpoint to create, update, delete,
recall, or redirect a shuttle — the fleet is seeded at startup and moved only by the
simulation. The only interactive elements are navigation links into §5.3.

#### Validation

None — no inputs.

#### Error handling

| Case | Display |
|---|---|
| First load fails | Full-page error state with `message` and a Retry button |
| A **poll** fails after a successful load | Keep rendering the last good data; show a dismissible amber banner *"Live updates interrupted — retrying."* Do not blank the dashboard |

#### Polling

**1000 ms**, started on mount and stopped on unmount. Never stops while the page is open —
an idle fleet can become busy at any moment because calls may come from elsewhere.

---

### 5.3 Travel Request Detail

Lets a passenger follow one call from `Queued` through to `Completed`.

**Route:** `/requests/$id`

#### Endpoint

```
GET /api/travel-requests/{id}
```

`{id}` is constrained to `:int` server-side. A non-integer path does not match the route at
all and produces a framework `404` with **no JSON body** — guard the param before fetching.

#### Response — `200 OK`

**(a) In transit** — note that `estimatedArrivalUtc` and `secondsUntilArrival` are populated
*only* in this state:

```json
{
  "id": 4,
  "status": "InTransit",
  "origin": "Angel 1",
  "destination": "Argus X",
  "lifeFormCount": 2,
  "totalWeightKg": 278.5,
  "lifeForms": [
    { "species": "Gorn", "weightKg": 210.5 },
    { "species": "Human", "weightKg": 68 }
  ],
  "requestedAtUtc": "2026-09-09T04:47:09.4936227",
  "completedAtUtc": null,
  "shuttleId": 3,
  "shuttleName": "Shuttle 3",
  "estimatedArrivalUtc": "2026-09-09T04:47:21.7763202",
  "secondsUntilArrival": 8.0534739,
  "rejectionReason": null
}
```

**(b) Completed:**

```json
{
  "id": 2,
  "status": "Completed",
  "origin": "Angel 1",
  "destination": "Boreth",
  "lifeFormCount": 2,
  "totalWeightKg": 95,
  "lifeForms": [
    { "species": "Human", "weightKg": 70 },
    { "species": "Ewok", "weightKg": 25 }
  ],
  "requestedAtUtc": "2026-09-09T04:46:37.7644465",
  "completedAtUtc": "2026-09-09T04:46:41.7669529",
  "shuttleId": 2,
  "shuttleName": "Shuttle 2",
  "estimatedArrivalUtc": null,
  "secondsUntilArrival": null,
  "rejectionReason": null
}
```

**(c) Rejected** — note `completedAtUtc` **is** set, because rejection is terminal:

```json
{
  "id": 3,
  "status": "Rejected",
  "origin": "Angel 1",
  "destination": "Aurelia",
  "lifeFormCount": 1,
  "totalWeightKg": 5000,
  "lifeForms": [
    { "species": "Gorn", "weightKg": 5000 }
  ],
  "requestedAtUtc": "2026-09-09T04:46:37.9422737",
  "completedAtUtc": "2026-09-09T04:46:37.9422737",
  "shuttleId": null,
  "shuttleName": null,
  "estimatedArrivalUtc": null,
  "secondsUntilArrival": null,
  "rejectionReason": "No shuttle can carry 1 life form(s) weighing 5000kg. The largest shuttle takes 20 life forms or 4000kg."
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | `int` | |
| `status` | `TravelRequestStatus` | All five values reachable |
| `origin` / `destination` | `string` | Planet **names**; `"Unknown"` if the id cannot be resolved |
| `lifeFormCount` | `int` | |
| `totalWeightKg` | `decimal` | |
| `lifeForms` | `{ species, weightKg }[]` | The full party |
| `requestedAtUtc` | `string` | Always present. `parseUtc` |
| `completedAtUtc` | `string \| null` | Set on **both** `Completed` and `Rejected` |
| `shuttleId` / `shuttleName` | `int? / string?` | Set once assigned; stay set after completion |
| `estimatedArrivalUtc` | `string \| null` | **Only** when `status === 'InTransit'` |
| `secondsUntilArrival` | `number \| null` | **Only** when `status === 'InTransit'` |
| `rejectionReason` | `string \| null` | Only when `Rejected` |

#### Response — `404 Not Found`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Travel request not found.",
  "status": 404,
  "detail": "No call with id 999999 exists.",
  "traceId": "00-c649f1600c2cf4b1202d1608e03104e4-6f6ef5facb1ac9ec-00"
}
```

Note this envelope has **`detail` but no `errors`** — the message to show the user is
`detail`, not `title`.

#### UI layout

**Header:** *"Call #{id}"* + status badge.

**Status timeline** — five nodes, current one highlighted:

```
Queued  →  Assigned  →  InTransit  →  Completed
                                   ↘  Rejected
```

Show `Rejected` as a terminal branch, greying the unreached nodes. `Queued` and `Assigned`
both mean "not yet flying"; only `InTransit` shows an ETA.

**Detail fields**

| Label | Field |
|---|---|
| From | `origin` |
| To | `destination` |
| Party size | `lifeFormCount` |
| Total weight | `totalWeightKg` + `" kg"` |
| Shuttle | `shuttleName` (links to `/`) or *"Not yet assigned"* when `null` |
| Requested | `formatUtc(requestedAtUtc)` |
| Completed | `formatUtc(completedAtUtc)` or `—` |
| ETA | `formatUtc(estimatedArrivalUtc)` + live countdown — **rendered only when `InTransit`** |

**Life forms table**

| Column | Field |
|---|---|
| Species | `species` |
| Weight (kg) | `weightKg` |

Footer row with the party total.

**Rejection banner** — when `rejectionReason !== null`, an amber panel above the details
showing the reason verbatim. It already names the fleet's ceilings, so no extra explanation
is needed.

**Queued notice** — when `status === 'Queued'`, an informational line: *"Waiting for a
shuttle to free up. This page updates automatically."*

#### Actions

**None.** No cancel, no edit, no delete — the API exposes no write for an existing request.
See §5.6. Provide only navigation: back to the fleet, and a link to the assigned shuttle.

#### Validation

None — no inputs.

#### Error handling

| Case | Display |
|---|---|
| `404` | Full-page empty state showing `detail` (*"No call with id 999999 exists."*) and a link back to `/` |
| Non-integer `id` in the URL | Do not call the API; render the same not-found state |
| Poll failure after a good load | Keep the last data; amber *"Live updates interrupted"* banner |

#### Polling

**1000 ms**, and **stop as soon as `status` is `Completed` or `Rejected`** — those are
terminal, so further requests can never change anything.

```ts
if (data && (data.status === 'Completed' || data.status === 'Rejected')) stopPolling();
```

> **Why polling matters here.** A call created as `Queued` becomes `Assigned` the moment a
> shuttle frees up, then `InTransit`, then `Completed` — typically within seconds, with no
> user action. During capture for this document, request 9 returned `"outcome": "Queued"`
> and polled back as `"status": "Assigned"` on the very next request.

---

### 5.4 Travel History

The persisted log the brief asks for: *"persist travel data history via api."* Only
**terminal** calls appear — `Completed` and `Rejected`. In-flight and queued calls are not
here.

**Route:** `/history`

#### Endpoint

```
GET /api/travel-history?page=1&pageSize=25
```

Full paging contract in §6.

#### Response — `200 OK`

Captured with `pageSize=2` to show a partial page:

```json
{
  "page": 1,
  "pageSize": 2,
  "totalCount": 3,
  "items": [
    {
      "id": 3,
      "travelRequestId": 2,
      "origin": "Angel 1",
      "destination": "Boreth",
      "lifeFormCount": 2,
      "totalWeightKg": 95,
      "outcome": "Completed",
      "shuttleId": 2,
      "requestedAtUtc": "2026-09-09T04:46:37.7644465",
      "recordedAtUtc": "2026-09-09T04:46:41.7669529",
      "rejectionReason": null
    },
    {
      "id": 2,
      "travelRequestId": 3,
      "origin": "Angel 1",
      "destination": "Aurelia",
      "lifeFormCount": 1,
      "totalWeightKg": 5000,
      "outcome": "Rejected",
      "shuttleId": null,
      "requestedAtUtc": "2026-09-09T04:46:37.9422737",
      "recordedAtUtc": "2026-09-09T04:46:37.9422737",
      "rejectionReason": "No shuttle can carry 1 life form(s) weighing 5000kg. The largest shuttle takes 20 life forms or 4000kg."
    }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | `int` | History row id — **not** the request id |
| `travelRequestId` | `int` | Links to §5.3 |
| `origin` / `destination` | `string` | Planet names, `"Unknown"` fallback |
| `lifeFormCount` | `int` | |
| `totalWeightKg` | `decimal` | |
| `outcome` | `'Completed' \| 'Rejected'` | In practice only these two are reachable |
| `shuttleId` | `int \| null` | `null` for rejected calls — nothing carried them |
| `requestedAtUtc` | `string` | When the passenger called |
| `recordedAtUtc` | `string` | When it became terminal. **Sort key** |
| `rejectionReason` | `string \| null` | Non-null only when `outcome === 'Rejected'` |

> **Ordering is fixed and not client-controllable:** `recordedAtUtc DESC, id DESC` — newest
> first. There are **no** sort, filter, search, date-range, or outcome-filter parameters on
> this endpoint. Do not build UI affordances that imply otherwise; filter client-side within
> the current page only, and label it as such.

#### UI layout

**Table columns**

| # | Column | Field | Rendering |
|---|---|---|---|
| 1 | # | `id` | Muted |
| 2 | Call | `travelRequestId` | Link to `/requests/{id}` |
| 3 | From | `origin` | |
| 4 | To | `destination` | |
| 5 | Life forms | `lifeFormCount` | Right-aligned |
| 6 | Weight | `totalWeightKg` | Right-aligned, `" kg"` |
| 7 | Outcome | `outcome` | Badge — `Completed` green, `Rejected` red |
| 8 | Shuttle | `shuttleId` | `"Shuttle {id}"` or `—` |
| 9 | Requested | `requestedAtUtc` | `formatUtc` |
| 10 | Recorded | `recordedAtUtc` | `formatUtc` |
| 11 | Reason | `rejectionReason` | Truncated with a title tooltip; `—` when null |

The table must scroll horizontally inside its own container on narrow screens rather than
forcing the page to scroll.

**Above the table:** total count (*"3 records"*) and the page-size selector.
**Below the table:** the pager (§6).

**Empty state** — `totalCount === 0` (the state on a fresh database): *"No trips recorded
yet. Completed and rejected calls appear here."* with a link to `/call`.

#### Actions

**Read-only.** No create, update, or delete. History rows are written by the backend when a
trip completes or a call is rejected; there is no endpoint to amend or remove one. See §5.6.

#### Validation

Only the paging parameters — see §6. The UI clamps them (§4.5), so a 400 from this page
means a hand-edited URL.

#### Error handling

| Case | Display |
|---|---|
| `400` on `Page` / `PageSize` | Banner with the message, and reset the search params to defaults |
| Other errors | Standard banner (§7) |

#### Polling

**None.** History only changes when a trip finishes, and this is a review screen rather than
a live one. Provide a manual **Refresh** button. Refetch automatically when `page` or
`pageSize` changes.

---

### 5.5 Planet Stats

The brief's stated reason for persistence: *"help the team to study most traveled planets
and deploy more shuttles in the future."*

**Route:** `/stats`

#### Endpoints

```
GET /api/travel-history/stats
GET /api/planets                 (also used by 5.1's selects)
```

#### Response — `GET /api/travel-history/stats` → `200 OK`

A bare array. **Every planet appears, including those with all-zero counts** — a zero is a
finding, and omitting the row would hide it.

```json
[
  {
    "planetId": 2,
    "planet": "Boreth",
    "distanceRank": 2,
    "completedTrips": 2,
    "rejectedCalls": 0,
    "lifeFormsDelivered": 4,
    "weightDeliveredKg": 190
  },
  {
    "planetId": 5,
    "planet": "Argus X",
    "distanceRank": 5,
    "completedTrips": 2,
    "rejectedCalls": 0,
    "lifeFormsDelivered": 3,
    "weightDeliveredKg": 381.5
  },
  {
    "planetId": 1,
    "planet": "Angel 1",
    "distanceRank": 1,
    "completedTrips": 0,
    "rejectedCalls": 0,
    "lifeFormsDelivered": 0,
    "weightDeliveredKg": 0
  },
  {
    "planetId": 3,
    "planet": "Aurelia",
    "distanceRank": 3,
    "completedTrips": 0,
    "rejectedCalls": 1,
    "lifeFormsDelivered": 0,
    "weightDeliveredKg": 0
  },
  {
    "planetId": 4,
    "planet": "Blue Horizon",
    "distanceRank": 4,
    "completedTrips": 0,
    "rejectedCalls": 0,
    "lifeFormsDelivered": 0,
    "weightDeliveredKg": 0
  }
]
```

| Field | Type | Notes |
|---|---|---|
| `planetId` | `int` | |
| `planet` | `string` | ⚠ The key is **`planet`**, not `planetName` |
| `distanceRank` | `int` | 1 = closest |
| `completedTrips` | `int` | Counts trips **to** this planet |
| `rejectedCalls` | `int` | Calls **to** this planet that no shuttle could carry |
| `lifeFormsDelivered` | `int` | Completed only |
| `weightDeliveredKg` | `decimal` | Completed only |

**Ordering:** `completedTrips DESC, distanceRank ASC`. Statistics are grouped by
**destination** planet only — there is no origin breakdown.

#### Response — `GET /api/planets` → `200 OK`

Ordered by `distanceRank` ascending. This is stable seed data:

```json
[
  { "id": 1, "name": "Angel 1", "distanceRank": 1 },
  { "id": 2, "name": "Boreth", "distanceRank": 2 },
  { "id": 3, "name": "Aurelia", "distanceRank": 3 },
  { "id": 4, "name": "Blue Horizon", "distanceRank": 4 },
  { "id": 5, "name": "Argus X", "distanceRank": 5 }
]
```

#### UI layout

**Summary tiles** — computed client-side by summing the array:

- Total completed trips
- Total rejected calls
- Total life forms delivered
- Busiest destination (`planet` of the first row)

**Table columns**

| # | Column | Field | Rendering |
|---|---|---|---|
| 1 | Planet | `planet` | |
| 2 | Distance | `distanceRank` | `"#1 (closest)"` … `"#5 (farthest)"` |
| 3 | Completed trips | `completedTrips` | Right-aligned + inline bar, scaled to the row max |
| 4 | Rejected calls | `rejectedCalls` | Right-aligned; red when `> 0` |
| 5 | Life forms delivered | `lifeFormsDelivered` | Right-aligned |
| 6 | Weight delivered | `weightDeliveredKg` | Right-aligned, `" kg"` |

**Planets reference table** (from `GET /api/planets`) — id, name, distance rank. Small, and
useful for confirming the ids used in §5.1.

> **[PDR decision]** Call out rows with `rejectedCalls > 0` visually. They answer the brief's
> question directly: a dock turning parties away is where the next shuttle should go.

#### Actions

**Read-only.** No create, update, or delete. Statistics are derived by the backend from
history; planets are immutable seed data with no write endpoint. See §5.6.

#### Validation

None — no inputs.

#### Error handling

Standard banner (§7). An empty database returns all five rows with zeroes rather than an
empty array, so there is no distinct empty state — but do show *"No trips recorded yet"*
under the table when every `completedTrips` is 0.

#### Polling

**None.** Fetch on mount; provide a manual **Refresh** button.

---

### 5.6 Actions matrix — what this API can and cannot do

The brief for this document asked for create, update, and delete actions per page. Stating
the situation plainly: **the backend implements exactly one write operation.** Every other
endpoint is a `GET`. There is no `PUT`, `PATCH`, or `DELETE` anywhere in
[`Features/`](../src/backend/SpaceTravel.Api/Features/).

| Resource | Create | Update | Delete |
|---|---|---|---|
| Travel request | ✅ `POST /api/travel-requests` (§5.1) | ❌ none | ❌ none |
| Shuttle | ❌ seeded at startup | ❌ moved only by the simulation | ❌ none |
| Planet | ❌ seeded from `appsettings.json` | ❌ none | ❌ none |
| Travel history | ❌ written by the backend on completion/rejection | ❌ none | ❌ none |

**Do not build UI for actions that do not exist.** Buttons that 404 are worse than absent
ones. Where a user might reasonably expect an action, show a disabled control with an
explanatory tooltip, or omit it entirely.

#### If these operations are wanted later — backend work required

Listed for planning only. **None of this exists; do not code against it.**

| Proposed endpoint | Purpose | Backend work implied |
|---|---|---|
| `DELETE /api/travel-requests/{id}` | Cancel a call that is still `Queued` | New terminal status or reuse `Rejected`; `Fleet` must drop it from the queue; a history row must be written |
| `PATCH /api/travel-requests/{id}` | Amend the party before departure | Re-run dispatch; must be rejected once `InTransit` |
| `POST /api/shuttles` | Add a shuttle to the fleet | Straightforward; `Fleet` already reads shuttles from the database |
| `PATCH /api/shuttles/{id}` | Take a shuttle out of service | New `ShuttleState`; the dispatcher must skip it; in-flight manifests need a policy |

Each would also need a validator, an OpenAPI annotation, and — for the two destructive ones
— a confirmation dialog in the UI.

---

## 6. Paging

> **Applies to `GET /api/travel-history` only.** `/api/shuttles`, `/api/planets`, and
> `/api/travel-history/stats` return complete, unpaged arrays. There is no paging anywhere
> else in the API.

### 6.1 Request

```
GET /api/travel-history?page=1&pageSize=25
```

| Parameter | Type | Default | Valid range | Message when invalid |
|---|---|---|---|---|
| `page` | `int` | `1` | `>= 1` | `Page starts at 1.` |
| `pageSize` | `int` | `25` | `1`–`100` inclusive | `Page size must be between 1 and 100.` |

Both are optional — `GET /api/travel-history` with no query string returns page 1 at size 25.

> **Naming.** The parameters bind from the record `GetTravelHistoryRequest(int Page = 1, int
> PageSize = 25)` via `[AsParameters]`, so the **OpenAPI document names them `Page` and
> `PageSize`** (PascalCase). Model binding is case-insensitive, so `page` / `pageSize` work
> identically and are what the repo's `.http` files use. **[PDR decision]** Send lowercase.
> Note that validation error *keys* come back PascalCase regardless (§4.2).

Server-side slicing is `Skip((page - 1) * pageSize).Take(pageSize)`.

### 6.2 Response

```json
{
  "page": 1,
  "pageSize": 2,
  "totalCount": 3,
  "items": [ /* TravelHistoryItem[] - see 5.4 */ ]
}
```

| Key | Type | Meaning |
|---|---|---|
| `page` | `int` | Echoes the requested page |
| `pageSize` | `int` | Echoes the requested size |
| `totalCount` | `int` | Total rows across **all** pages |
| `items` | `array` | This page's rows; `[]` when empty, never `null` |

> ⚠ **Gotcha — there is no `totalPages`.** The envelope has exactly the four keys above.
> Derive it:
>
> ```ts
> const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
> ```
>
> The `Math.max(1, …)` matters: with `totalCount === 0` the naive formula yields 0, and a
> pager reading "Page 1 of 0" looks broken.

There is also **no** `hasNext` / `hasPrevious`. Derive those too:

```ts
const hasPrevious = page > 1;
const hasNext = page < totalPages;
```

#### Requesting a page past the end

Not an error. The API returns an empty `items` array with a truthful `totalCount`:

```json
{ "page": 99, "pageSize": 25, "totalCount": 5, "items": [] }
```

**[PDR decision]** Detect this (`items.length === 0 && totalCount > 0`) and redirect to
`totalPages` rather than showing a confusing empty table.

#### Invalid parameters

`GET /api/travel-history?page=0&pageSize=500` → `400 Bad Request`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Page": ["Page starts at 1."],
    "PageSize": ["Page size must be between 1 and 100."]
  },
  "traceId": "00-e3ab661345b5df25a2598f124b9d7024-539febecaa8ac1d1-00"
}
```

### 6.3 Paging UI

**[PDR decision]** A `Pager` component rendered below the table.

```
Showing 1-2 of 3        [10 ▾] per page        ‹ Prev   Page 1 of 2   Next ›
```

| Control | Behaviour |
|---|---|
| **Prev** | Disabled when `page === 1`. Navigates to `page - 1` |
| **Next** | Disabled when `page >= totalPages`. Navigates to `page + 1` |
| **Page indicator** | `"Page {page} of {totalPages}"` using the derived value |
| **Range text** | `"Showing {(page-1)*pageSize + 1}-{min(page*pageSize, totalCount)} of {totalCount}"`. Replace with *"No records"* when `totalCount === 0` |
| **Page size selector** | `<select>` with `10 / 25 / 50 / 100`. **Must reset `page` to 1 on change** — otherwise a user on page 4 of 10-per-page jumps past the end at 100-per-page |

All of it lives in the URL, so pages are shareable and the browser back button works:

```ts
const navigate = useNavigate({ from: '/history' });

const goToPage = (page: number) =>
  navigate({ search: (prev) => ({ ...prev, page }) });

const changePageSize = (pageSize: number) =>
  navigate({ search: () => ({ page: 1, pageSize }) });  // note the reset
```

Because `validateSearch` (§4.5) clamps both values, the UI cannot itself produce a 400 —
but a hand-edited URL can, which is why §6.2 documents the body.

> **[PDR decision] Do not offer "jump to last page" or numbered page links.** `totalCount`
> supports them, but with fixed newest-first ordering and no filters they add clutter
> without answering a real question.

---

## 7. Error Handling Appendix

> ⚠ **Gotcha — the API returns three structurally different error bodies.** They come from
> three independent mechanisms, and a parser that assumes one shape will show `undefined`
> to users on the other two. §4.2 implements the unified parser; this section is the
> reference behind it.

### 7.1 Envelope 1 — validation failure (`400`)

**Produced by:** [`ValidationFilter.cs`](../src/backend/SpaceTravel.Api/Code/Validation/ValidationFilter.cs)
on `POST /api/travel-requests` and `GET /api/travel-history`.
**Content-Type:** `application/problem+json`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "DestinationPlanetId": ["Destination must differ from the planet you are calling from."]
  },
  "traceId": "00-67d430aa59d0b94f641c672edf8aa15e-e5c068261e98a7d7-00"
}
```

- **The distinguishing key is `errors`** — only this envelope has it.
- `title` is generic; the useful text is inside `errors`.
- Values are always arrays of strings — one entry per failed rule.
- **Keys are PascalCase C# property paths**, including array indexers
  (`LifeForms[0].WeightKg`). Payload properties are camelCase, so a mapping step is
  mandatory (§4.2).

There is a second, rarer form from the same filter when the body cannot be bound at all:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Malformed request",
  "status": 400,
  "detail": "The request body could not be bound to CallShuttleRequest."
}
```

### 7.2 Envelope 2 — not found (`404`)

**Produced by:** `Results.Problem(...)` in
[`GetTravelRequestEndpoint.cs`](../src/backend/SpaceTravel.Api/Features/GetTravelRequest/GetTravelRequestEndpoint.cs).
**Content-Type:** `application/problem+json`

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Travel request not found.",
  "status": 404,
  "detail": "No call with id 999999 exists.",
  "traceId": "00-c649f1600c2cf4b1202d1608e03104e4-6f6ef5facb1ac9ec-00"
}
```

- **`detail` present, `errors` absent.** Show `detail` — it names the specific id.

### 7.3 Envelope 3 — unhandled exception (`400` / `409` / `500`)

**Produced by:** [`GlobalExceptionHandler.cs`](../src/backend/SpaceTravel.Api/Code/Errors/GlobalExceptionHandler.cs).
**Content-Type:** `application/json` — **not** `problem+json`, and **not** a `ProblemDetails`
object. It is a hand-rolled anonymous type, so it has **no `detail` and no `errors`**.

```json
{
  "type": "https://datatracker.ietf.org/doc/html/rfc9110",
  "title": "Shuttle Shuttle 1 cannot accept this party without breaching its capacity.",
  "status": 409,
  "traceId": "0HN7E9K2P1QRS:00000003"
}
```

Status mapping:

| Exception | Status | `title` |
|---|---|---|
| `ArgumentException` (incl. `ArgumentOutOfRangeException`) | `400` | The raw exception message |
| `InvalidOperationException` | `409` | The raw exception message |
| anything else | `500` | `An unexpected error occurred.` |

- On `400`/`409` the `title` **is** the human-readable message.
- On `500` the message is deliberately generic — no stack trace leaks.
- `traceId` here is `HttpContext.TraceIdentifier` (format `0HN…`), unlike the W3C
  `00-…-…-00` form in envelopes 1 and 2. Both are just opaque support identifiers.

> ⚠ **Gotcha — malformed JSON yields `500`, not `400`.** The only launch profile sets
> `ASPNETCORE_ENVIRONMENT=Development`, which makes `RouteHandlerOptions.ThrowOnBadRequest`
> default to `true`. A syntactically invalid body throws `BadHttpRequestException`, which
> matches neither switch arm above and surfaces as a `500`. Always send well-formed JSON;
> if you are debugging a mystery 500 on `POST`, check your body first.

### 7.4 Normalization

| Source | → `ApiError.message` | → `ApiError.fieldErrors` |
|---|---|---|
| Envelope 1 | `title` (generic) | `errors`, keys normalized to camelCase |
| Envelope 1 (malformed body) | `detail` | `{}` |
| Envelope 2 | `detail` | `{}` |
| Envelope 3 | `title` | `{}` |
| Network failure | *"Cannot reach the API…"* | `{}` |

Resolution order — `detail || title || "Request failed (N)."` — covers all three, because
only envelope 2 and the malformed-body case populate `detail`.

### 7.5 Display rules

**[PDR decision]**

| Condition | Presentation |
|---|---|
| `error.hasFieldErrors` | **Inline only**, under each matching input. Never also show a banner — duplicating the message reads as two separate failures |
| `status === 404` on a detail route | Full-page empty state, not a banner |
| `status === 409` | Banner. The `title` is already user-readable |
| `status === 500` | Banner: *"An unexpected error occurred."* + `traceId` in small print |
| `status === 0` (network) | Banner naming the likely cause: the backend is not running |
| Poll failure after a successful load | Non-blocking amber banner; **keep rendering stale data** |

Two absolute rules:

1. **Never block a page on a failed poll.** Stale data beats an error screen when the
   previous load succeeded.
2. **Never show a raw HTTP status code to a user.** Every envelope carries a message; use it.

---

## 8. Appendix

### 8.1 Seed data

Written at startup by [`DbSeeder.cs`](../src/backend/SpaceTravel.Api/Data/DbSeeder.cs) from
`appsettings.json`. Seeding is **idempotent** — it is skipped when rows already exist — and
`spacetravel.db` persists between runs, so these ids are stable unless the file is deleted.

**Planets**

| Id | Name | DistanceRank |
|---|---|---|
| 1 | Angel 1 | 1 (closest) |
| 2 | Boreth | 2 |
| 3 | Aurelia | 3 |
| 4 | Blue Horizon | 4 |
| 5 | Argus X | 5 (farthest) |

**Shuttles** — all start `Idle` at Angel 1 (id 1), each with capacity **20 life forms or
4000 kg**:

| Id | Name |
|---|---|
| 1 | Shuttle 1 |
| 2 | Shuttle 2 |
| 3 | Shuttle 3 |
| 4 | Shuttle 4 |

Travel requests and history start empty.

> Because the database persists, a shuttle will often **not** be at Angel 1 when you start
> developing — it is wherever the last session left it. Delete `spacetravel.db` to reset.

### 8.2 Dispatch rules — so the UI can explain outcomes

From [`Fleet.Dispatch`](../src/backend/SpaceTravel.Api/Domain/Fleet.cs). Evaluated in order:

| # | Rule | Outcome |
|---|---|---|
| 0 | No shuttle could **ever** carry this party (exceeds 20 life forms or 4000 kg) | `Rejected`, terminal, recorded in history immediately |
| 1 | A shuttle is already committed to this exact origin→destination, has not yet left the pickup dock, and has room | `Assigned` — batched onto that shuttle |
| 2 | Otherwise the nearest idle empty shuttle, by `abs(distanceRank difference)` | `Assigned` |
| 3 | Everything is busy | `Queued` (FIFO; retried on every simulation tick) |

Rule 1 is the brief's *"smarts to be efficient when picking up passengers"* — one launch
instead of two is the largest fuel saving available. Surfacing it is worthwhile: when a
shuttle's manifest holds more than one request, the UI is showing consolidation working.

**Timing:** the simulation ticks every **500 ms**; a leg takes
`max(1, abs(rankFrom - rankTo)) × 3 seconds`. Angel 1 → Argus X is 4 ranks = 12 s.

### 8.3 Enum values

Both cross the wire as PascalCase **strings**, never integers.

| `ShuttleState` | Meaning |
|---|---|
| `Idle` | Parked at `currentPlanet`, available |
| `EnRoute` | Flying; arrives at `arrivesAtUtc` |
| `Arrived` | Touched down this tick; transient, settles to `Idle` |

| `TravelRequestStatus` | Meaning | Terminal |
|---|---|---|
| `Queued` | Waiting for a shuttle, FIFO | No |
| `Assigned` | A shuttle is committed but has not departed with it | No |
| `InTransit` | Aboard a shuttle in flight | No |
| `Completed` | Delivered | **Yes** |
| `Rejected` | No shuttle could ever carry it | **Yes** |

### 8.4 References

| Resource | Location |
|---|---|
| OpenAPI document | `http://localhost:5095/openapi/v1.json` (Development only) |
| Ready-made request examples | [`src/backend/SpaceTravel.Tests/api_request/`](../src/backend/SpaceTravel.Tests/api_request/) — 6 `.http` files |
| Endpoint implementations | [`src/backend/SpaceTravel.Api/Features/`](../src/backend/SpaceTravel.Api/Features/) |
| Validators | `Features/CallShuttle/CallShuttleValidator.cs`, `Features/GetTravelHistory/GetTravelHistoryValidator.cs` |
| Business rules | [`src/backend/SpaceTravel.Api/Domain/Fleet.cs`](../src/backend/SpaceTravel.Api/Domain/Fleet.cs) |
| Running the backend | [`how-to-run.md`](how-to-run.md) |

### 8.5 Notes on the OpenAPI document

Added alongside this PDR so the frontend can generate types instead of transcribing them.
Three caveats, all handled by the narrowing layer in §4.1:

1. **`/health` uses a named `HealthResponse` record** rather than an anonymous object, purely
   so it carries a real schema. The response is byte-identical: `{"status":"healthy"}`.
2. **A `NumericSchemaTransformer` is registered.** .NET types every numeric as
   `["integer","string"]` — true of the *binder*, false of anything the API emits. Without
   it, every id, count, and weight generates as `number | string`. The transformer strips the
   `string` half while preserving the `null` half, so `int?` stays `["null","integer"]`.
3. **The document does not describe everything.** FluentValidation rules, the `409`/`500`
   envelope, and the `201`-means-rejected semantics are all invisible to it. §5, §6, and §7
   of this document are the authority on those; the schema is the authority on field names
   and shapes.
