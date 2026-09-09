# Space Travel — Vertical Slice Monolith

Build the OVO Space Travel backend as a single ASP.NET Core monolith organised in
four folders (`Features`, `Domain`, `Code`, `Data`), with a rich domain model that
owns the shuttle-dispatch smarts and Minimal API endpoints that do nothing but
validate and delegate.

## Context

The repo is greenfield — only `README.md` (the challenge brief) and `docs/`
analysis exist, no code. [docs/brd-analysis.md](docs/brd-analysis.md) is the sole
input: it decomposes the brief into entities, a shuttle state machine, business
rules, and 16 flagged ambiguities with default assumptions.

The brief asks for a system that is "bug free, scalable, future proofed and
resilient" and "easy to run (no complex database systems)". The design below
answers that with a deliberately small monolith: business rules live in one
place (the `Fleet` aggregate) so they can be unit-tested without a database,
each API concern is a self-contained vertical slice so features can be added
without touching each other, and persistence is a single SQLite file so
`dotnet run` is the only setup step.

All previous architecture work in `docs/architecture-options.md` and
`docs/backend-implementation-spec.md` is superseded and ignored per the user's
instruction.

### Decisions locked with the user

| Decision | Choice |
|---|---|
| Stack | .NET 10, Minimal APIs, EF Core 10 + SQLite (file DB, real migrations), FluentValidation |
| Domain shape | `Fleet` aggregate root over Shuttles + queue; rich `Shuttle`/`TravelRequest` entities; value objects for capacity/life forms |
| Dispatch policy | Batch-first, then nearest idle, then queue FIFO |
| Concurrency | Serialized dispatch through a single in-process `SemaphoreSlim(1,1)` |
| Travel simulation | `BackgroundService` tick advances En Route → Arrived |
| Party model | `TravelRequest` owns a collection of `LifeForm` value objects; count and total weight are **derived** |
| History grain | One row per travel-request outcome (Completed **or** Rejected) |
| Slices | CallShuttle, GetFleet, GetPlanets, GetTravelRequest, GetTravelHistory, GetTravelStats |
| Tests | Domain unit tests + slice integration tests |
| Front end | Out of scope for this plan |

> **Stack deviation (recorded during Phase 1):** the plan was approved as .NET 9, but
> no 9.0 SDK is installed on this machine (5.0.201, 8.0.319, 10.0.100 only). The user
> chose **net10.0 with EF Core 10**, and the global `dotnet-ef` tool was upgraded
> 8.0.30 → 10.0.12. Resolved package versions: EF Core Sqlite/Design 10.0.12,
> FluentValidation.DependencyInjectionExtensions 12.1.1, xunit 2.9.3.
> **Anyone running this repo needs the .NET 10 SDK.**

### Assumptions carried from the BRD analysis

Adopted as-is (§7 of the analysis): no auth (#1, #2); one call = one party (#3);
species is a display label, weight is required positive kg (#4); one-way legs
(#5); ordinal planet distance as the fuel proxy (#7); a read API is enough for
stats (#8); fleet size and planet list are configuration, not magic numbers (#9);
calls are never split across shuttles (#10); origin ≠ destination enforced (#11);
rejected attempts are persisted alongside completed trips (#12); queue when no
shuttle fits (#13); dispatch is atomic (#14); no de-duplication of repeat calls (#16).

Assumption #15 (durable persistence) is satisfied structurally rather than with an
outbox: the simulation tick completes a trip and writes its history row inside the
same `SaveChanges` transaction, so a trip cannot complete without its history
existing. No separate retry machinery is needed in a single-process monolith.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

---

## Target Structure

```
SpaceTravel.sln
src/SpaceTravel.Api/                 ← the monolith; the four folders live here
  Program.cs                         ← composition root only
  appsettings.json                   ← planets, fleet, simulation options
  Features/                          ← 1 folder per slice, 5 files each
    CallShuttle/
      CallShuttleEndpoint.cs         ← route + validation filter + delegate
      CallShuttleRequest.cs
      CallShuttleResponse.cs
      CallShuttleValidator.cs
      CallShuttleHandler.cs          ← the only place with logic
      CallShuttleMapping.cs          ← request→domain, domain→response
    GetFleet/ GetPlanets/ GetTravelRequest/
    GetTravelHistory/ GetTravelStats/
  Domain/                            ← pure C#, zero EF/ASP.NET references
    Fleet.cs                         ← AGGREGATE ROOT: Dispatch + AdvanceTo
    Shuttle.cs                       ← rich entity, internal mutators
    TravelRequest.cs                 ← rich entity, owns LifeForms
    LifeForm.cs Capacity.cs          ← value objects
    Planet.cs TravelHistoryEntry.cs
    ShuttleState.cs TravelRequestStatus.cs DispatchOutcome.cs
  Code/                              ← infrastructure
    Endpoints/IEndpoint.cs, EndpointExtensions.cs
    Validation/ValidationFilter.cs
    Dispatch/IDispatchGate.cs, DispatchGate.cs
    Simulation/SimulationTickService.cs
    Time/IClock.cs, SystemClock.cs
    Errors/GlobalExceptionHandler.cs
    Options/FleetOptions.cs, PlanetOptions.cs, SimulationOptions.cs
  Data/
    SpaceTravelDbContext.cs
    FleetRepository.cs               ← composes the Fleet aggregate from rows
    Configurations/*Configuration.cs
    Migrations/
    DbSeeder.cs
tests/SpaceTravel.Tests/
  Domain/  Features/
```

### API contract

| Method | Route | Slice |
|---|---|---|
| POST | `/api/travel-requests` | CallShuttle — validate party, dispatch or queue |
| GET | `/api/travel-requests/{id}` | GetTravelRequest — Queued/Assigned/InTransit/Completed/Rejected |
| GET | `/api/shuttles` | GetFleet — live state, location, manifest, remaining capacity |
| GET | `/api/planets` | GetPlanets — 5 docks in distance order |
| GET | `/api/travel-history` | GetTravelHistory — paged outcome log |
| GET | `/api/travel-history/stats` | GetTravelStats — completed + rejected counts per destination |

Every endpoint follows the same flow: **route → `ValidationFilter<TRequest>` → handler**.
Handlers may use their slice's mapping, `SpaceTravelDbContext`, and services from
`Code/`. Handlers never contain business rules — they orchestrate.

### Dispatch rules (in `Domain/Fleet.cs`, ordered)

```
Rule 0  party.Count > 20 OR party.TotalWeightKg > 4000
        → Rejected("no shuttle can ever carry this party")     [edge case #2]

Rule 1  BATCH: a shuttle that will next be standing at the origin dock
        (Idle@origin, Arrived@origin, or flying an empty repositioning
        leg to origin) whose PlannedDestination is null or == destination,
        and whose manifest + party fits BOTH caps
        → load it; prefer the fullest such shuttle

Rule 2  NEAREST IDLE: empty Idle shuttle minimising
        |rank(shuttle.Location) - rank(origin)|, tie-break lowest Id
        → at origin: load now (departs next tick)
        → elsewhere: depart an empty repositioning leg to origin

Rule 3  → Queued (FIFO, re-dispatched by the tick as shuttles free up)
```

**Why batching works without a hold-timeout:** a loaded shuttle departs on the
*next* simulation tick, not instantly, and a repositioning shuttle is in flight
for a real duration. Both windows let a second call join the same trip naturally,
so no artificial "wait and fill" rule is introduced.

**Leg duration** = `max(1, |rank(from) − rank(to)|) × SimulationOptions.BaseLegDuration`
(default 3s). Ordinal planet rank is the only distance data the brief provides.

---

## Phase 1: Scaffold + `Code/` infrastructure
Status: Complete

- [x] Copy this plan to `plans/space-travel-vertical-slice.md` in the repo root and continue maintaining it there
- [x] `dotnet new sln -n SpaceTravel`; `dotnet new web -o src/SpaceTravel.Api` (net10.0); `dotnet new xunit -o tests/SpaceTravel.Tests`; add both to the solution
- [x] Add packages to the API: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, `FluentValidation.DependencyInjectionExtensions`
- [x] ~~Create the four empty folders with a `.gitkeep` each~~ — skipped as unnecessary; `Code/` is populated and the other three are populated in Phases 2, 3 and 5
- [x] `Code/Time/IClock.cs` + `SystemClock` (`DateTime UtcNow`) — registered singleton so tests can freeze time
- [x] `Code/Endpoints/IEndpoint.cs` (`static abstract void Map(IEndpointRouteBuilder)`) + `EndpointExtensions.MapEndpoints()` scanning the assembly, so a new slice self-registers
- [x] `Code/Validation/ValidationFilter.cs` — an `IEndpointFilter` resolving `IValidator<TRequest>` and returning `TypedResults.ValidationProblem` on failure
- [x] `Code/Errors/GlobalExceptionHandler.cs` implementing `IExceptionHandler` → RFC 7807 ProblemDetails; wire `AddProblemDetails()` + `UseExceptionHandler()`
- [x] `Code/Options/` — `FleetOptions`, `PlanetOptions`, `SimulationOptions`; bound in `Program.cs` from `appsettings.json`
- [x] Configure structured logging via the built-in `ILogger` + `AddHttpLogging()` (no third-party sink)
- [x] `Program.cs` composition root: options, logging, problem details, `MapEndpoints()`, `/health` returning 200

### Verification Plan
- `dotnet build` → 0 errors, 0 warnings
- `dotnet run --project src/SpaceTravel.Api` then `curl -s http://localhost:5000/health` → HTTP 200
- `grep -r "using Microsoft.EntityFrameworkCore" src/SpaceTravel.Api/Code/` → no matches (Code layer stays persistence-agnostic)

### Phase Summary

**Done.** Solution `SpaceTravel.sln` with `src/SpaceTravel.Api` (ASP.NET Core empty
web, net10.0) and `tests/SpaceTravel.Tests` (xunit, references the API). The `Code/`
layer is complete: `IClock`/`SystemClock`, the `IEndpoint` self-registration
convention, `ValidationFilter<T>` + a `.WithValidation<T>()` route extension,
`GlobalExceptionHandler`, and the three options classes. `Program.cs` is a
composition root only. `appsettings.json` carries the 5 planets, 4 shuttles, the
20/4000 caps and the simulation timings — no magic numbers in code.

**Verification results (all pass):** `dotnet build` → Build succeeded, 0 Warning(s),
0 Error(s). App booted on `http://localhost:5080`, `GET /health` →
`STATUS 200 BODY {"status":"healthy"}`. Grep for `using Microsoft.EntityFrameworkCore`
under `Code/` → no files found.

**Key decisions.**
- Target framework is **net10.0**, not the planned net9.0 — see the stack-deviation
  note near the top. `dotnet-ef` was upgraded globally to 10.0.12.
- `Program.cs` ends with `public partial class Program;` so `WebApplicationFactory<Program>`
  can boot the app in Phase 6.
- The `20` / `4000` caps live in `FleetOptions`, not in `Domain`. `Capacity` in Phase 2
  is constructed from those values rather than hardcoding them, satisfying assumption #9.
- `GlobalExceptionHandler` maps `ArgumentException` → 400 and `InvalidOperationException`
  → 409, so domain guard violations surface as client errors rather than 500s.
- Validators are registered with `includeInternalTypes: true`, so slice validators may
  be `internal`.

---

## Phase 2: `Domain/` — the rich model
Status: Complete

No EF Core, ASP.NET, or `Code/` references in this folder — it must compile and be
testable in isolation.

- [x] `Capacity.cs` — value object holding `MaxLifeForms` (20) and `MaxWeightKg` (4000); `bool Accommodates(int count, decimal weightKg)` returns true only when **both** ceilings hold (business rule 3, edge case #3)
- [x] `LifeForm.cs` — value object `(string Species, decimal WeightKg)`; guard `WeightKg > 0` in the factory
- [x] `Planet.cs` — `Id, Name, DistanceRank`; `int DistanceTo(Planet other)` = `|rank difference|`
- [x] `TravelRequest.cs` — rich entity: `Id, OriginId, DestinationId, RequestedAtUtc, Status, AssignedShuttleId, LifeForms`. `Count` and `TotalWeightKg` are **computed** from `LifeForms`. Static factory `Create(origin, destination, lifeForms, now)` rejects empty parties and `origin == destination`. State mutators (`MarkAssigned`, `MarkInTransit`, `MarkCompleted`, `MarkRejected(reason)`) are `internal` so only `Fleet` may call them
- [x] `Shuttle.cs` — rich entity: `Id, Name, CurrentPlanetId, FlyingToPlanetId, State, PlannedDestinationId, DepartedAtUtc, ArrivesAtUtc, Manifest`. Behaviour: `CanAccept`, `IsServing`, `Assign`, `DepartEmptyTo`, `DepartLoaded`, `Arrive`, `Unload`, `Settle`. All mutators `internal`
- [x] `ShuttleState.cs` (`Idle`, `EnRoute`, `Arrived`) and `TravelRequestStatus.cs` (`Queued`, `Assigned`, `InTransit`, `Completed`, `Rejected`) per BRD §3a
- [x] `DispatchOutcome.cs` — discriminated result: `Assigned(shuttleId)`, `Queued`, `Rejected(reason)`
- [x] `TravelHistoryEntry.cs` — immutable record of a terminal request: origin, destination, life-form count, total weight, shuttle, requested/completed timestamps, outcome, rejection reason
- [x] `Fleet.cs` — **aggregate root**. Holds shuttles, planets, and the queued requests. Implements `DispatchOutcome Dispatch(TravelRequest, DateTime now)` using the ordered rules above, and `IReadOnlyList<TravelHistoryEntry> AdvanceTo(DateTime now)` which: completes arrivals (unload → Completed + history entry), loads waiting parties onto shuttles that arrived on a repositioning leg, departs loaded Idle shuttles, then re-dispatches the queue FIFO

### Verification Plan
- `dotnet build` → 0 errors
- `grep -rE "EntityFrameworkCore|Microsoft.AspNetCore" src/SpaceTravel.Api/Domain/` → no matches
- Temporary scratch console/test asserting `Fleet.Dispatch` on a fresh fleet returns `Assigned`, and a 21-life-form party returns `Rejected` (formalised in Phase 6)

### Phase Summary

**Done.** Ten files under `Domain/`, no EF or ASP.NET references. `Fleet` is the
aggregate root and the only public way to move anything: every mutator on `Shuttle`
and `TravelRequest` is `internal`, so a handler physically cannot overbook a shuttle
or skip a state. `Fleet.Dispatch` implements the four ordered rules;
`Fleet.AdvanceTo` runs the five-step tick (land → unload → settle → drain queue →
launch) and returns the `TravelHistoryEntry` rows for the caller to persist.

**Verification results (all pass):** `dotnet build` → 0 errors, 0 warnings. Grep for
`EntityFrameworkCore|Microsoft.AspNetCore` under `Domain/` → no files found.
`dotnet test` → 3 passed, 0 failed (432 ms, no database touched), covering
assign-on-first-call, reject-21-life-forms, and the full
dispatch → load → fly → arrive → unload → history lifecycle.

**Key decisions.**
- **Manifest is homogeneous** — every request on a shuttle shares one origin and one
  destination. No multi-stop routing; the brief never asks for it, and it reduces
  "is this shuttle going my way?" to one comparison. Recorded for `docs/assumptions.md`.
- **Three shuttle fields track position**: `CurrentPlanetId` (null while flying),
  `FlyingToPlanetId` (physical target), `PlannedDestinationId` (the trip it is
  committed to). A repositioning leg is exactly `FlyingToPlanetId != PlannedDestinationId`,
  which is how `HasDeliveredParty` tells a pickup arrival from a delivery arrival.
- **`Dispatch` never moves a shuttle; `AdvanceTo` never assigns one** (except via the
  queue drain, which calls `Dispatch`). This split is what creates the batching window:
  a shuttle assigned by an HTTP call sits stationary until the next tick, and a
  repositioning shuttle is collectable for its whole flight — so `WillCollectFrom`
  can batch a second party on without any artificial hold-and-fill rule.
- **`Create` factories take an optional `id`** (default 0 → database assigns). This is
  the seam that lets unit tests build a 5-planet, 4-shuttle world with distinct ids
  and no database. `tests/.../Domain/FleetTestData.cs` is that world.
- Rejections are stamped by `Dispatch` itself (`MarkRejected`), so the handler only
  has to persist the history row — the domain decides, the slice records.

---

## Phase 3: `Data/` — persistence
Status: Complete

- [x] `SpaceTravelDbContext.cs` — `DbSet<Planet>`, `DbSet<Shuttle>`, `DbSet<TravelRequest>`, `DbSet<TravelHistoryEntry>`; `ApplyConfigurationsFromAssembly` in `OnModelCreating`
- [x] `Configurations/PlanetConfiguration.cs` — unique index on `Name` and on `DistanceRank`
- [x] `Configurations/ShuttleConfiguration.cs` — enum-to-string for `State`; `OwnsOne(Capacity)`; three FKs to `Planet`; manifest navigation forced to field access
- [x] `Configurations/TravelRequestConfiguration.cs` — `OwnsMany(r => r.LifeForms)` into a `LifeForms` table; enum-to-string `Status`; index on `Status`; field-access on the backing collection
- [x] `Configurations/TravelHistoryEntryConfiguration.cs` — index on `DestinationPlanetId` for the stats `GROUP BY`, plus a unique index on `TravelRequestId`
- [x] `FleetRepository.cs` — `Task<Fleet> LoadAsync(ct)` eager-loads planets, shuttles with manifests, and queued requests, then constructs the `Fleet`. Children are EF-tracked, so `Fleet` mutations persist on `SaveChangesAsync` — no manual diffing
- [x] `DbSeeder.cs` — `MigrateAsync()` then seed planets and shuttles from `PlanetOptions`/`FleetOptions` if the tables are empty (all shuttles start `Idle` at Angel 1)
- [x] Register `AddDbContext<SpaceTravelDbContext>(o => o.UseSqlite(...))`; connection string in `appsettings.json`; run the seeder at startup
- [x] `dotnet ef migrations add InitialCreate --project src/SpaceTravel.Api --output-dir Data/Migrations`
- [x] Add `*.db`, `*.db-shm`, `*.db-wal` to `.gitignore`

### Verification Plan
- `dotnet ef migrations list --project src/SpaceTravel.Api` → shows `InitialCreate`
- ~~`sqlite3 ...` row counts~~ — replaced by `tests/SpaceTravel.Tests/Data/DbSeederTests.cs`, which
  applies the migration to a real temp SQLite file and asserts the counts. Windows
  PowerShell 5.1 cannot load .NET 10 assemblies and no `sqlite3` CLI is installed, so a
  test is both the only autonomous option here and the more durable one.
- `dotnet run --project src/SpaceTravel.Api` creates and seeds `spacetravel.db`
- Restart the app → counts stay `5` and `4` (seeder is idempotent)

### Phase Summary

**Done.** `Data/` holds the DbContext, four entity configurations, `FleetRepository`,
`DbSeeder` and the `InitialCreate` migration. Domain classes map straight to tables —
there are no separate persistence models. `Program.cs` migrates and seeds at startup,
so `dotnet run` is genuinely the only step to a working server, which is what the
brief's "no complex database systems" asks for.

**Verification results (all pass):** `dotnet ef migrations list` →
`20260909004559_InitialCreate`. `dotnet test` → 5 passed, 0 failed, including the two
new seeder tests (world seeded correctly; seeding twice does not duplicate).
`dotnet run` logged `Seeded 5 planets.` and
`Seeded 4 shuttles at Angel 1, each carrying up to 20 life forms or 4000kg.`

**Two problems found and fixed.**
1. **Decimals were mapping to SQLite `TEXT`**, which silently breaks `SUM` and
   `ORDER BY` — it would have quietly corrupted the stats slice in Phase 5. All three
   weight properties now use `.HasConversion<double>()` and store as `REAL`. The
   migration was regenerated, so no bad schema was ever committed.
2. **`DbSeeder` gave all four shuttles one shared `Capacity` instance.** `Capacity` is
   an owned entity, so EF needs one instance per owner; sharing it made EF write the
   columns for none of them and startup died on
   `NOT NULL constraint failed: Shuttles.MaxLifeForms`. Each shuttle now gets its own.

**Worth knowing.** EF Core 10 turns on SQLite WAL by default, so after a run
`spacetravel.db` stays at 4096 bytes while the data sits in `spacetravel.db-wal`
until checkpoint. That is normal — do not read a small `.db` file as an empty database.

**Key decisions.**
- `FleetRepository` loads queued requests separately from shuttle manifests
  (`Status == Queued` vs. the `AssignedShuttleId` FK), so no request is tracked twice.
- All `Planet` FKs use `DeleteBehavior.Restrict` — history must never be cascade-deleted.
- Enums persist as text so a future state cannot silently renumber existing rows.
- `TravelHistory` has a unique index on `TravelRequestId`: one terminal outcome per
  call, enforced by the database rather than by convention.

---

## Phase 4: Dispatch orchestration — gate + simulation tick
Status: Complete

- [x] `Code/Dispatch/IDispatchGate.cs` + `DispatchGate.cs` — singleton wrapping `SemaphoreSlim(1,1)`, exposing `Task<IDisposable> AcquireAsync(ct)`
- [x] `Code/Simulation/SimulationTickService.cs` — `BackgroundService` on a `PeriodicTimer`: create a DI scope, acquire the gate, `FleetRepository.LoadAsync`, `fleet.AdvanceTo(clock.UtcNow)`, add returned history entries, `SaveChangesAsync`, release
- [x] Wrap each tick body in try/catch so a transient failure logs and the loop survives (the "resilient" NFR)
- [x] Register the gate as a singleton, the tick service via `AddHostedService`, `FleetRepository` scoped
- [x] Add an `xunit`-friendly switch: `SimulationOptions.Enabled` (default `true`) so integration tests can disable the background loop and drive `AdvanceTo` deterministically

### Verification Plan
- `dotnet run` with `Logging:LogLevel:SpaceTravel=Debug`; after seeding, no shuttle moves (nothing queued) and the log shows a periodic tick with no errors for 30s
- After Phase 5, POST a call to Argus X and watch the log show `Idle → EnRoute → Arrived → Idle` within the expected `rank × 3s` window

### Phase Summary

**Done.** `DispatchGate` is a single process-wide `SemaphoreSlim(1,1)` behind
`IDispatchGate`; both the `CallShuttle` handler and the tick take it for the whole
read-decide-write, so the capacity check and the reservation are one atomic step.
`SimulationTickService` runs every 500ms and is the only thing that moves shuttles.

**Verification results (all pass):** app ran 12s (~24 ticks) with the tick logging
`Simulation tick started at 00:00:00.5000000 intervals, 00:00:03 per distance step.`
and zero errors or exceptions. The second item was deferred to Phase 5 and passed
there: a call to Argus X (4 rank steps × 3s) reported `secondsUntilArrival` 11.96 and
had completed by the 16s mark.

**Key decisions.**
- A completed trip and its history row are added in the same `SaveChangesAsync`, so a
  trip cannot be delivered without being recorded — assumption #15 without an outbox.
- The tick catches and logs per-iteration exceptions rather than letting one bad pass
  kill the loop.
- `SaveChangesAsync` runs only when `ChangeTracker.HasChanges()`, so an idle fleet
  costs reads and no writes.

---

## Phase 5: `Features/` — the six vertical slices
Status: Complete

Each slice is one folder with endpoint, request, response, validator, handler,
mapping. No slice references another slice.

- [x] **CallShuttle** — `POST /api/travel-requests`. Validator: origin and destination are known planet ids, `origin != destination` (rule 4, #11), at least one life form, every `WeightKg > 0` (edge case #5). Handler: acquire the dispatch gate → `FleetRepository.LoadAsync` → map request to `TravelRequest` → `fleet.Dispatch(request, clock.UtcNow)` → persist a `TravelHistoryEntry` immediately if the outcome is `Rejected` (#12) → `SaveChangesAsync` → map to response (`201 Created` with the request id + outcome, or `400` on validation failure)
- [x] **GetTravelRequest** — `GET /api/travel-requests/{id}`; returns status, assigned shuttle, ETA when in transit; `404` when unknown
- [x] **GetFleet** — `GET /api/shuttles`; per shuttle: state, current or destination planet, ETA, manifest summary, remaining seats and remaining kg
- [x] **GetPlanets** — `GET /api/planets`; the 5 docks ordered by `DistanceRank`
- [x] **GetTravelHistory** — `GET /api/travel-history?page=&pageSize=`; validator caps `pageSize` at 100; newest first
- [x] **GetTravelStats** — `GET /api/travel-history/stats`; `GROUP BY` destination returning completed and rejected counts, ordered by completed descending — the brief's "most traveled planets"
- [x] Register all `IValidator`s via `AddValidatorsFromAssemblyContaining<Program>()`
- [x] Confirm every endpoint with a request body/query applies `WithValidation<TRequest>()`. `GetPlanets`, `GetFleet` and `GetTravelStats` take no input and `GetTravelRequest` takes only a typed route id, so they have no validator by design

### Verification Plan
- `dotnet run`, then:
  - `curl -s -X POST localhost:5000/api/travel-requests -H "Content-Type: application/json" -d '{"originPlanetId":1,"destinationPlanetId":5,"lifeForms":[{"species":"Vulcan","weightKg":68}]}'` → `201` with `outcome: "Assigned"`
  - same body with `"destinationPlanetId":1` → `400` ProblemDetails naming the origin/destination rule
  - a party of 21 life forms → `201` with `outcome: "Rejected"` and a reason
  - one life form at `weightKg: 5000` → `201` with `outcome: "Rejected"`
  - `curl -s localhost:5000/api/shuttles` → 4 shuttles, one no longer `Idle`
  - wait ~15s, then `curl -s localhost:5000/api/travel-history/stats` → Argus X shows `completed: 1`

### Phase Summary

**Done.** Six slices under `Features/`, each self-registering through `IEndpoint`.
The end-to-end script is kept at `scripts/e2e.ps1`.

**Verification results — all 15 checks pass** (run against a fresh database):

| # | Check | Result |
|---|---|---|
| 1 | `GET /api/planets` | 200, 5 planets in distance order |
| 2 | Angel 1 → Argus X | 201 `Assigned` to Shuttle 1 |
| 3 | Same-planet call | 400 "Destination must differ from the planet you are calling from." |
| 4 | Unknown planet 99 | 400 "Destination must be one of the known planets." |
| 5 | Zero life forms | 400 "A call must include at least one life form." |
| 6 | Negative weight | 400 on `LifeForms[0].WeightKg` |
| 7 | Party of 21 | 201 `Rejected`, reason names the 20/4000 limits |
| 8 | Single 5000kg life form | 201 `Rejected` |
| 9 | `GET /api/shuttles` | 200; Shuttle 1 `EnRoute`, ETA 11.96s, 19 seats / 3932kg spare |
| 10–11 | After 16s, `GET /api/travel-requests/1` | 200 `Completed`, shuttle 1 named |
| 12 | `GET /api/travel-history/stats` | Argus X `completed: 1`; Aurelia and Blue Horizon `rejected: 1` each |
| 13 | `GET /api/travel-history` | 3 entries — 1 completed, 2 rejected |
| 14 | `pageSize=5000` | 400 "Page size must be between 1 and 100." |
| 15 | Unknown request id | 404 ProblemDetails |

**One real bug found and fixed.** The first e2e run returned a *Completed* request with
`shuttleId: null` — the passenger record forgot which shuttle flew them. Cause:
`Shuttle.Manifest` was an EF `HasMany` relationship, so `Unload()` clearing the
collection made EF null each request's `AssignedShuttleId`. The manifest is now
`Ignore`d by EF and rebuilt by `FleetRepository` from active requests, while
`AssignedShuttleId` is a plain FK owned by `TravelRequest` that is set once and never
cleared. Re-verified: request 1 now reports `shuttleId: 1, shuttleName: "Shuttle 1"`
after completion. The migration was regenerated, so no bad schema was committed.

**Key decisions.**
- **A rejection returns 201, not 400.** The call is a real, retrievable, persisted
  record whose business outcome happens to be "no shuttle can carry this". 400 is
  reserved for malformed input. This is what makes rejections analysable per #12.
- **Validators check shape, the Fleet checks feasibility.** "Is planet 99 real?" is a
  validator question; "will this party fit?" is the domain's. No capacity logic leaked
  into `Features/`.
- **`GetFleetHandler` reuses `FleetRepository`** rather than re-querying, so the UI can
  never show a fleet state that disagrees with what the dispatcher sees.
- **The stats slice lists every planet, including zeroes** — a destination nobody
  travels to is a finding, and omitting the row would hide it.

---

## Phase 6: Tests + run documentation
Status: Complete

- [x] Reference the API project from `tests/SpaceTravel.Tests`; add `Microsoft.AspNetCore.Mvc.Testing`. FluentAssertions was **not** added — xunit's own assertions cover everything here and FluentAssertions is no longer free for commercial use
- [x] `Domain/CapacityTests.cs` — the dual cap is joint: 4 life forms at 3900 kg + a 150 kg fifth is rejected on weight despite 16 free seats (business rule 3, edge case #3)
- [x] `Domain/FleetDispatchTests.cs` — 11 tests across all four rules, including never splitting a party (#10) and deterministic tie-breaking
- [x] `Domain/FleetAdvanceTests.cs` — 7 tests on a frozen clock: the full state machine, one history row per aboard request, history emitted exactly once, repositioning legs, and mid-approach batching
- [x] `Features/CallShuttleTests.cs` — 15 tests over `WebApplicationFactory` with a per-run SQLite file and the tick disabled
- [x] `Features/TravelHistoryTests.cs` — 5 tests: completed and rejected outcomes recorded distinctly, stats grouped per destination, batching visible through the API, empty-state, and paging
- [x] Write `docs/how-to-run.md`
- [x] Write `docs/assumptions.md`

### Verification Plan
- `dotnet test` → all tests pass, 0 failures
- `dotnet test --filter FullyQualifiedName~Domain` → passes with no SQLite file created (proves the domain is persistence-free)
- `dotnet build -warnaserror` → succeeds

### Phase Summary

**Done.** 52 tests: 30 pure-domain, 22 through the real HTTP stack. Plus
`docs/how-to-run.md` and `docs/assumptions.md`, the latter being the brief's
explicitly requested call-out of assumptions.

**Verification results (all pass):**
- `dotnet build -warnaserror` → 0 Warning(s), 0 Error(s)
- `dotnet test` → 52 passed, 0 failed (3s)
- `dotnet test --filter "FullyQualifiedName~SpaceTravel.Tests.Domain"` → 30 passed in
  **184 ms**, with a before/after count confirming **zero** SQLite files created —
  the domain really is persistence-free, not just nominally separated.

**Key decisions.**
- **`FleetAdvancer` was extracted** from `SimulationTickService` so the tick body is a
  scoped service both the background loop and the tests call. Tests advance the clock
  explicitly rather than sleeping, which is why the integration suite is deterministic
  and finishes in seconds.
- **The tick is disabled in tests** via `Simulation:Enabled=false`, so nothing moves
  underneath an assertion.
- Each integration test that counts rows constructs its own factory, so it owns its own
  database and the counts are exact.
- FluentAssertions was dropped from the plan (licensing); xunit assertions sufficed.

---

## Final Recap

A complete space-travel booking and dispatch backend: a single ASP.NET Core monolith
in four folders, with the business rules concentrated in one aggregate and the API
surface split into independent vertical slices.

**What was built**

- **`Domain/`** — 10 files, zero framework references. `Fleet` is the aggregate root
  and the only public way to change anything; `Shuttle` and `TravelRequest` are rich
  entities whose mutators are `internal`, so no handler can overbook a shuttle or skip
  a state. The dual 20/4000 capacity cap, the four-rule dispatch policy, the queue and
  the three-state shuttle machine all live here and are testable with no database.
- **`Features/`** — six slices (`CallShuttle`, `GetTravelRequest`, `GetFleet`,
  `GetPlanets`, `GetTravelHistory`, `GetTravelStats`), each a folder with its own
  endpoint, request, response, validator, handler and mapping. Slices never reference
  each other and self-register through `IEndpoint`, so adding a feature never touches
  `Program.cs`. Every endpoint follows one flow: validate → handler.
- **`Code/`** — the clock, the dispatch gate, the simulation tick and `FleetAdvancer`,
  the validation filter, the ProblemDetails handler, and the options that keep the
  planet list, fleet size, capacity caps and timings out of the code.
- **`Data/`** — DbContext, four entity configurations mapping the domain straight to
  SQLite with no separate persistence models, `FleetRepository`, the seeder and one
  migration.

**How the brief's requirements are met**

| Requirement | How |
|---|---|
| 4 shuttles, 20 life forms **or** 4000 kg | `Capacity.Accommodates` checks both ceilings jointly; fleet size and caps are configuration |
| 5 planets, each with a dock | Seeded from config in distance order; `Planet` is both destination and origin dock |
| Passengers call a shuttle | `POST /api/travel-requests` |
| "Smarts" to conserve fuel | Ordered dispatch: reject-impossible → batch onto a shuttle already going that way → nearest idle → queue |
| Persist travel history via API | Every terminal outcome becomes a `TravelHistoryEntry`, committed in the same transaction as the trip |
| Study most-travelled planets | `GET /api/travel-history/stats`, including rejection counts — the signal for where the next shuttle should go |
| Easy to run, no complex DB | One SQLite file, migrated and seeded on startup; `dotnet run` is the only step |
| Bug free | 52 tests, `-warnaserror` clean; two real defects found and fixed during the build |

**Defects found and fixed while building** (each recorded in its phase summary):

1. `decimal` was mapping to SQLite `TEXT`, which would have silently broken `SUM` and
   `ORDER BY` in the stats slice. Now `REAL`.
2. `DbSeeder` shared one `Capacity` instance across four shuttles, so EF persisted it
   for none of them and startup crashed.
3. Modelling the manifest as an EF relationship meant unloading a party nulled the
   request's `AssignedShuttleId` — a delivered passenger forgot which shuttle carried
   them. The manifest is now rebuilt by `FleetRepository` and the FK is never cleared.

**Deviation from the approved plan:** the plan specified .NET 9, which is not installed
on this machine. With the user's agreement the project targets **net10.0** with EF Core
10, and the global `dotnet-ef` tool was upgraded 8.0.30 → 10.0.12. Anyone running the
repo needs the .NET 10 SDK.

**Out of scope, as agreed:** no front end. The brief's UI requirement is unmet by
design; `GET /api/shuttles` and `GET /api/planets` exist to feed one.

**Known limits** are stated plainly in `docs/assumptions.md`: single-process dispatch
lock, simulated travel time, no multi-stop routing, no authentication, and a
one-tick (~500 ms) batching window for two walk-up calls at the same dock.

## Deployment Plan

This is a take-home exercise, so "deployment" means handing over a repo a reviewer can
run in one command.

**1. Confirm a clean tree.**

```bash
dotnet build -warnaserror     # expect: 0 Warning(s), 0 Error(s)
dotnet test                   # expect: 52 passed, 0 failed
git status                    # expect: no spacetravel.db* (they are gitignored)
```

**2. Commit and push.** The repo is currently on `master` with everything uncommitted.
Nothing has been committed during this work — that is the next step and needs the
user's go-ahead.

```bash
git add .
git commit -m "feat: space travel dispatch backend (vertical slice monolith)"
git push
```

**3. What a reviewer does.** Only the .NET 10 SDK is needed:

```bash
git clone <repo> && cd bnd-spc
dotnet run --project src/SpaceTravel.Api
```

The app migrates, seeds and serves. Point them at
[`docs/how-to-run.md`](../docs/how-to-run.md) for the walkthrough and
[`docs/assumptions.md`](../docs/assumptions.md) for the called-out assumptions the
brief asks for. `scripts/e2e.ps1` exercises all 15 behaviours end to end against a
running instance on port 5090.

**4. If it were to go to a real environment**, in order of necessity:

- Replace the in-process `DispatchGate` with a distributed lock, or pin the service to
  one instance. This is the hard blocker on running more than one replica.
- Move off SQLite to a server database (the EF configurations port as-is; only the
  `UseSqlite` call and the `HasConversion<double>()` weight mappings would change).
- Add authentication before exposing travel history publicly.
- Replace startup `MigrateAsync` with a deliberate migration step in the release
  pipeline, so a bad migration cannot take the app down on boot.

---

## Known trade-offs to state in `docs/assumptions.md`

- **Single-process dispatch lock.** Correct here, but it is the one thing that
  blocks horizontal scale-out. The `IDispatchGate` seam exists so it can be
  swapped for a distributed lock without touching `Domain/` or `Features/`.
- **Simulated travel time.** `rank difference × 3s` is a stand-in; the brief gives
  ordinal distances only, no speeds or fuel figures.
- **Nearest-idle can out-rank fuel savings.** Edge case #6 in the analysis is
  resolved in favour of the nearest idle shuttle; the rules are an ordered,
  readable list in `Fleet.cs` precisely so this is tunable.
- **No authentication.** Per assumptions #1 and #2; every endpoint is anonymous.

## Final Recap
_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan
_(write when all phases complete: step-by-step deployment instructions)_
