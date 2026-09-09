---
paths:
  - "src/frontend/**"
---

# Frontend rules — Space Travel SPA (Vite + React 19 + TS strict)

**The spec is [`docs/frontend-pdr.md`](../../docs/frontend-pdr.md)** — every endpoint there has a
*captured* request/response, not an invented example. Read the relevant §5 page section before
building a page. Items marked **[PDR decision]** there are ours to change; everything else describes
what the API actually does. The app lives at `src/frontend/` (the PDR writes `frontend/`).

## Stack — do not substitute

TanStack Router (file-based) · Zustand · Tailwind **v4** · `openapi-typescript`.
No TanStack Query, no axios, no Redux, no component library.

- **Tailwind v4 is configured in CSS**, via `@import "tailwindcss"` + `@theme { --color-* }` in
  `src/index.css`. Never create `tailwind.config.js` or `postcss.config.js` — `@tailwindcss/vite` handles it.
- Status colours are theme tokens (`--color-idle|enroute|arrived|queued|rejected`), used as
  `bg-enroute`/`text-rejected`. Add a token rather than a hex literal.
- `src/api/schema.d.ts` is **generated** — never hand-edit. Regenerate with `npm run gen:api`
  (`openapi-typescript http://localhost:5095/openapi/v1.json -o src/api/schema.d.ts`) **while the
  backend is running**, and re-run it after any backend change. `src/api/types.ts` is the hand-written
  narrowing layer over it.

## Layout

```
src/api/      schema.d.ts (generated) · types.ts · client.ts (apiFetch + ApiError) · endpoints.ts
src/stores/   usePlanetsStore · useFleetStore · useTravelRequestStore · useHistoryStore
src/components/  StatusBadge · CapacityBar · FieldError · Pager · ErrorBanner · Countdown
src/lib/      dates.ts (parseUtc) · polling.ts (createPoller)
src/routes/   __root · index (fleet) · call · requests.$id · history · stats
```

## The five things that will bite you

1. **A rejected call returns `201 Created`.** Branch on the `outcome` / `status` field, never on
   HTTP status. Rejection is a business outcome with a `rejectionReason` and a real id to poll.
2. **Responses are camelCase; validation error *keys* are PascalCase** (`DestinationPlanetId`,
   `LifeForms[0].WeightKg`). `normalizeFieldKey` in `client.ts` maps them back — extend it, plus its
   `INDEXED` regex, whenever a new field can fail validation.
3. **`*Utc` timestamps often arrive without a `Z`** (C# `DateTime` round-tripped through SQLite).
   Always parse with `parseUtc()` from `lib/dates.ts`; `new Date(raw)` silently shifts by the local
   offset. There is no acceptable exception to this.
4. **The world moves without you** — a 500 ms server tick advances shuttles; a leg is
   `max(1, |rankDifference|) × 3s`. No WebSocket/SSE/SignalR exists. **Polling is the only option.**
5. **The backend has no CORS.** All fetches use **relative paths** (`fetch('/api/shuttles')`), and the
   Vite dev proxy forwards `/api` + `/openapi` to `http://localhost:5095`. Never hardcode the origin,
   and don't introduce `VITE_API_BASE_URL` for dev.

## Data access

- Every request goes through `apiFetch<T>` — it normalises the API's **three** error envelopes
  (validation `errors`, `detail`, `title`) into one `ApiError { status, message, fieldErrors, traceId }`.
  Never call `fetch` directly from a component; never `catch` and swallow into a bare string.
- One typed function per endpoint in `api/endpoints.ts`; components call stores, stores call endpoints.
- Stores share one shape: `{ data, loading, error, refresh, startPolling, stopPolling }`.
  **`loading` is true only on the first load** — a spinner on every poll makes the dashboard unreadable;
  keep showing stale data and surface poll failures in a non-blocking banner.
- Polling belongs to the store via `createPoller`, started on mount and stopped on unmount, so no
  interval outlives its page. Intervals: fleet 1000 ms; one travel request 1000 ms **and stop when
  `status` is `Completed`/`Rejected`**; planets fetched once and cached forever (seed data is immutable);
  history and stats refetch on demand only.

## Pages & routing

- Route files map 1:1 to PDR §5 pages: `/` fleet dashboard, `/call`, `/requests/$id`, `/history`, `/stats`.
- `/history` owns `page` + `pageSize` in the URL via `validateSearch`, clamped to `page >= 1` and
  `pageSize ∈ {10,25,50,100}` so the UI never sends what the API would reject. Paging is
  `totalCount`-based with a `Pager` component; no numbered page links, no "jump to last".
- Error display: field errors go under their input via `FieldError`; a 404 on a detail route is a
  full-page empty state, not a banner; everything else is a banner.
- Capacity is dual — show both seats and kilograms (`CapacityBar`), and warn (advisory, non-blocking)
  when a party looks unfittable. The server is the authority on whether it fits.
- The brief asks for a UI that *illustrates how pickup and the shuttle system work* — make dispatch
  outcomes legible (why batched, why queued, why rejected). PDR §8.2 lists the rules in UI language.
  No animation is required.

## Don't

- Don't add auth, i18n, SSR, offline support, or file upload — the backend has none of it.
- Don't change the backend to suit the frontend (CORS, status codes) without saying so explicitly;
  the PDR's contract is captured from a running instance.
- Don't loosen `strict: true`, and don't `any` your way past a generated type — narrow it in `types.ts`.
