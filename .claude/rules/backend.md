---
paths:
  - "src/backend/**"
---

# Backend rules — SpaceTravel.Api (.NET 10, Minimal APIs, EF Core + SQLite)

## Adding a feature = adding one folder

`Features/<Verb><Noun>/` containing only what the slice needs:

| File | Required? | Job |
|---|---|---|
| `<Name>Endpoint.cs` | yes | `IEndpoint` with `static void Map` — route, `.WithValidation<T>()`, `.WithName()`, `.WithSummary()`, `.Produces<T>()` |
| `<Name>Request.cs` / `<Name>Response.cs` | yes for writes | `sealed record`s; response records live next to the endpoint that returns them |
| `<Name>Validator.cs` | only if there is input | FluentValidation `AbstractValidator<T>`; may inject `SpaceTravelDbContext` |
| `<Name>Handler.cs` | only if it mutates or is non-trivial | orchestration only; registered in `Program.cs` as scoped |
| `<Name>Mapping.cs` | when mapping is non-trivial | `internal static`, request→domain and domain→response |

Read-only slices (`GetPlanets`, `GetTravelStats`) skip the handler and query `AsNoTracking()`
straight from the endpoint. Do not add a handler just for symmetry.

Never edit `Program.cs` to register a route — reflection finds `IEndpoint`. Do edit it to register
a new handler, validator assembly scan is already global, or an options section.

## Domain (`Domain/`)

- **`Fleet` is the only aggregate root and the only place business rules live.** `Dispatch` and
  `Advance` are its two entry points. If a rule about capacity, batching, queue order, or state
  transitions is being added, it goes here — not in a handler, validator, or endpoint.
- Entities: `private` ctor for EF + `public static Create(...)` factory that guards its invariants
  (`ArgumentException`/`ArgumentOutOfRangeException`). All setters `private set`.
- **Mutators on `Shuttle`/`TravelRequest` are `internal`** so only `Fleet` can move them. Keep them
  internal; if you need a new transition, add it as an internal method plus a `Fleet` method that calls it.
- `Capacity` is a value-object `record` enforcing the dual cap — a load fits only when **both**
  ceilings hold (`Accommodates`). Never compare against one cap alone.
- `DispatchOutcome` is a closed hierarchy (`Assigned | Queued | Rejected`) with a private ctor;
  match exhaustively.
- Derived, never stored: `LifeFormCount`, `TotalWeightKg`, `Used*`, `Remaining*`.
- Dispatch order is deliberate and tested — reject impossible → batch onto a shuttle already serving
  that exact origin→destination → nearest idle shuttle (distance rank is the fuel proxy) → queue FIFO.
  Changing this order changes the brief's "smarts"; update `FleetDispatchTests` and `docs/assumptions.md`.

## Handler shape

```csharp
using var _ = await _gate.AcquireAsync(ct);   // ALWAYS, for anything that reads-then-writes the fleet
var now = _clock.UtcNow;                      // never DateTime.UtcNow directly
var fleet = await _fleetRepository.LoadAsync(ct);
var outcome = fleet.Dispatch(request, now);    // one domain call
await _db.SaveChangesAsync(ct);                // children are tracked; no diffing, no mapping back
```

Constructor injection with explicit fields (no primary constructors in handlers — match existing
style). Log one structured `LogInformation` per outcome. Terminal outcomes get a `TravelHistoryEntry`.

## Persistence (`Data/`)

- One `IEntityTypeConfiguration<T>` per entity in `Data/Configurations/`; discovered by
  `ApplyConfigurationsFromAssembly`. Use them to reach private setters and backing fields
  (`Metadata.SetPropertyAccessMode(PropertyAccessMode.Field)`) — the domain keeps its invariants.
- **SQLite has no decimal.** Money/weight columns need `.HasConversion<double>()` or EF falls back
  to TEXT and silently breaks `SUM`/`ORDER BY`. Enums use `.HasConversion<string>()`.
- Schema changes need a migration: `dotnet ef migrations add <Name> --project src/backend/SpaceTravel.Api`.
  Startup runs the seeder, which is idempotent — never make it insert unconditionally.
- `FleetRepository.LoadAsync` rebuilds the aggregate from tracked rows and splits active requests
  into manifests vs. queue. Any new request status must be handled there too.

## Tests (`SpaceTravel.Tests`)

- `Domain/` tests touch no database: build fleets with `FleetTestData` helpers and pass explicit ids.
  Prefer these — they are the fast, honest safety net for every rule.
- `Features/` tests boot the real app via `SpaceTravelApiFactory` (throwaway SQLite file,
  `Simulation:Enabled=false`) and move time with `AdvanceAsync(now)` / `RunToCompletionAsync()`.
  Never `Task.Delay` to wait for the tick.
- Test names are sentences: `A_second_call_for_the_same_trip_batches_onto_the_same_shuttle`.
- `dotnet test` must pass before you report done.

## Don't

- Don't add CORS, auth, MediatR, AutoMapper, a repository per entity, or a second DB provider —
  each was considered and rejected in `docs/architecture-options.md` / `docs/nfr-analysis.md`.
- Don't return a non-2xx for a business rejection, and don't change `/api/travel-requests` to 200 —
  `docs/frontend-pdr.md` documents the captured contract the SPA is built against.
- Don't expose `/openapi` outside Development, or annotate schemas by hand — `.Produces<T>()` is enough.
- Don't commit `spacetravel.db*`, `bin/`, `obj/`, or `.vs/`.
