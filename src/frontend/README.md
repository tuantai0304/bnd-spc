# Space Travel — frontend

React SPA over the Space Travel API. The specification is
[`docs/frontend-pdr.md`](../../docs/frontend-pdr.md); the working rules are
[`.claude/rules/frontend.md`](../../.claude/rules/frontend.md).

## Run

The API must be running first — the dev server proxies to it.

```bash
# terminal 1
dotnet run --project src/backend/SpaceTravel.Api    # http://localhost:5095

# terminal 2
cd src/frontend && npm install && npm run dev       # http://localhost:5173
```

| Script | Purpose |
|---|---|
| `npm run dev` | Vite dev server with the `/api` proxy |
| `npm run build` | Typecheck (`tsc -b`) then bundle |
| `npm run typecheck` | Types only |
| `npm run lint` | oxlint |
| `npm run gen:api` | Regenerate `src/api/schema.d.ts` — **backend must be running** |

## Pages

| Route | Page |
|---|---|
| `/` | Fleet dashboard — live shuttle state, capacity, manifests |
| `/call` | Call a shuttle (the only write in the app) |
| `/requests/$id` | One call, from `Queued` through to `Completed` |
| `/history` | Paged log of completed and rejected calls |
| `/stats` | Trips per destination planet |

## Five things that will bite you

1. **A rejected call returns `201 Created`.** Branch on `outcome` / `status`, never on the
   HTTP status. Rejection is a business outcome with a reason and a real id to poll.
2. **Responses are camelCase; validation error keys are PascalCase**
   (`LifeForms[0].WeightKg`). `normalizeFieldKey` in `api/client.ts` maps them back —
   extend it, and its `INDEXED` regex, whenever a new field can fail validation.
3. **`*Utc` timestamps often arrive with no `Z`.** Always use `parseUtc()` from
   `lib/dates.ts`. `new Date(raw)` silently shifts by your UTC offset — locally that was
   ten hours.
4. **The world moves without you.** A 500 ms server tick advances shuttles and there is no
   WebSocket, SSE, or SignalR. Polling is the only option; it lives in the stores.
5. **The backend has no CORS.** Every fetch uses a relative path and the Vite proxy makes
   it same-origin. Never hardcode the origin.

## Structure

```
src/api/      schema.d.ts (generated) · types.ts (narrowing) · client.ts · endpoints.ts
src/stores/   usePlanetsStore · useFleetStore · useTravelRequestStore · useHistoryStore
src/components/  StatusBadge · CapacityBar · FieldError · Pager · ErrorBanner · Countdown
src/lib/      dates.ts (parseUtc) · polling.ts (createPoller)
src/routes/   __root · index · call · requests.$id · history · stats
```

Components call stores; stores call `api/endpoints`; everything goes through `apiFetch`.
Tailwind v4 is configured in `src/index.css` — there is deliberately no `tailwind.config.js`
and no `postcss.config.js`.

### Two generated files

- **`src/api/schema.d.ts`** — from the backend's OpenAPI document. Never hand-edit;
  re-run `npm run gen:api` after any backend change. `src/api/types.ts` is the
  hand-written narrowing layer over it (enums, nullability).
- **`src/routeTree.gen.ts`** — written by the TanStack Router plugin. It is **committed**
  so that `npm run build` works on a clean clone, where `tsc -b` runs before Vite would
  otherwise generate it. After adding or renaming a route file, run `npm run dev` (or
  `npx vite build`) once to regenerate it before relying on `npm run typecheck`.
