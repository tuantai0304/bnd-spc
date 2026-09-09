# Architecture Options — OVO Space Frontend Coding Challenge

**Source documents:** [`brd-analysis.md`](brd-analysis.md), [`nfr-analysis.md`](nfr-analysis.md)
**Analysis type:** Solution-architecture proposal — 2-3 candidate architectures
for the space-dock/dispatch system, each scored against the confirmed BRD
business rules and NFR targets, with a recommendation.

**Stack scoping (confirmed):** .NET / C# backend across all options; frontend
framework is chosen per option where it's a meaningful differentiator. All
options cover the full stack (frontend structure + backend), since the BRD
explicitly requires a UI ("every planet to have its own space dock... UI to
illustrate how the passenger pickup and shuttle system works,"
[brd-analysis.md §1](brd-analysis.md)).

**How to read this document:** each option is scored against the same six
dimensions, then against the five NFR categories from `nfr-analysis.md`
(Availability, Performance, Scalability, Security, Cost). §4 covers Option D
(Vertical Slice Architecture), whose infrastructure is identical to Option A
— see its opening note for why it's scored differently from B/C. The
comparison table in §5 gives the side-by-side view; §6 gives the
recommendation.

---

## 1. Option A — Modular Monolith (Recommended)

### 1.1 Architecture style
A single deployable ASP.NET Core application, internally organized into
independent modules with enforced boundaries (no direct cross-module class
references — only through interfaces/mediator messages), so modules could be
extracted into services later without a rewrite. This directly implements the
"scalable, future-proofed" NFR goal without paying the operational cost of
distribution today, which the NFRs (single container, $0 cost, demo scale) say
isn't needed yet.

### 1.2 Project structure and module boundaries

```
SpaceAcademy.sln
├── src/
│   ├── SpaceAcademy.Api/              # ASP.NET Core host: controllers/minimal APIs,
│   │                                     SignalR hub, DI composition root, Dockerfile
│   ├── SpaceAcademy.Modules.Fleet/     # Shuttle entities, capacity rules (20 pax / 4000kg),
│   │                                     in-memory fleet-state store, shuttle state machine
│   │                                     (Idle → En Route → Arrived, per brd-analysis.md §3a)
│   ├── SpaceAcademy.Modules.Dispatch/  # Dispatch algorithm (brd-analysis.md §4.2): scores
│   │                                     candidate shuttles, performs the atomic
│   │                                     check-and-reserve, owns the FIFO queue for
│   │                                     no-shuttle-available (Edge Case #1)
│   ├── SpaceAcademy.Modules.Planets/   # Planet/dock catalog, configurable list + distance
│   │                                     rank (brd-analysis.md §7.9)
│   ├── SpaceAcademy.Modules.History/   # TravelRequest/TravelHistory persistence (EF Core +
│   │                                     SQLite), stats aggregation query for §4.3
│   ├── SpaceAcademy.Shared.Contracts/  # Cross-module DTOs/events (e.g. CallRequested,
│   │                                     ShuttleAssigned, TripCompleted) — the only thing
│   │                                     modules reference from each other
│   └── SpaceAcademy.Web/               # Frontend (React + Vite, or Blazor Server — see 1.6)
├── tests/
│   ├── SpaceAcademy.Modules.Dispatch.Tests/   # capacity/atomicity/concurrency tests —
│   │                                             directly verifies Edge Cases #2-5, #9
│   └── SpaceAcademy.Modules.Fleet.Tests/
└── docker-compose.yml                  # single service; SQLite file on a mounted volume
```

Modules communicate via in-process mediator messages (e.g. MediatR) carried in
`Shared.Contracts`, not direct references — this is the concrete mechanism
that keeps the "modular" promise real rather than aspirational, and is what
would let e.g. `Modules.Dispatch` be lifted into its own service later
(NFR scalability's "config-only growth path" doesn't force this, but the BRD's
own "future-proofed" language does deserve *an* answer, and this is the
cheapest one that costs nothing today).

**The key design decision — where dispatch state lives:** the fleet is fixed
at 4 shuttles (configurable, defaulting to 4). Rather than reading/writing
shuttle state through SQLite on every call (a DB round-trip on the hot path),
`Modules.Fleet` holds the live shuttle state (location, status, current
manifest) as an **in-memory, thread-safe singleton** guarded by a single lock
around the check-and-reserve operation. This is what makes Edge Case #4
("dispatch must be atomic") trivial to get right — a single in-process lock
around 4 objects — instead of needing DB-level transactions or optimistic
concurrency, and it's what gets the p50 < 100ms / p95 < 300ms dispatch targets
in [nfr-analysis.md §2](nfr-analysis.md) comfortably, since the decision never
leaves process memory. SQLite is used purely as the **durable system of
record** for completed/rejected `TravelRequest`s (the history/analytics data
the BRD actually asks to persist,
[brd-analysis.md §5 rule 5](brd-analysis.md)) — every dispatch decision is
written through to SQLite before being acknowledged (satisfies the RPO-zero
target in [nfr-analysis.md §1](nfr-analysis.md)), but the *decision itself*
never blocks on disk I/O.

### 1.3 Database choice and schema approach
**SQLite** via EF Core Code-First migrations — directly implements Business
Rule 6 / the Cost NFR's "no complex DB setup, embedded/file-based DB bundled
in the same container" ([brd-analysis.md §5.6](brd-analysis.md),
[nfr-analysis.md §5](nfr-analysis.md)). WAL journal mode is enabled for
concurrent-read/single-writer behavior, matching the single-writer nature of
the in-memory dispatch lock above.

Schema (minimal, matches the entities in
[brd-analysis.md §3](brd-analysis.md)):

| Table | Key columns |
|---|---|
| `Planets` | `Id`, `Name`, `DistanceRank` (seeded with the 5 named planets; configurable per [brd-analysis.md §7.9](brd-analysis.md)) |
| `Shuttles` | `Id`, `Name` (seeded with 4; capacity constants configurable) |
| `TravelRequests` | `Id`, `OriginPlanetId`, `DestinationPlanetId`, `ShuttleId` (nullable until assigned), `Status` (`Queued`/`Assigned`/`EnRoute`/`Completed`/`Rejected` — covers Edge Case #12's failed-attempt logging), `RequestedAtUtc`, `CompletedAtUtc` |
| `TravelRequestPassengers` | `Id`, `TravelRequestId`, `Species`, `WeightKg` (child rows implement the "one call = a party of 1+ life forms" model from [brd-analysis.md §7.3](brd-analysis.md)) |

No migration framework beyond EF Core is needed; `dotnet ef database update`
(or auto-migrate on startup) is the entire setup step — matches "running the
server should be easy" verbatim ([README.md](../README.md)).

### 1.4 API design approach
**REST** for the call/history CRUD surface (`POST /api/calls`,
`GET /api/planets`, `GET /api/shuttles`, `GET /api/history/stats`), plus a
**SignalR hub** (`/hubs/fleet`) pushing shuttle state-machine transitions
(Idle/En Route/Arrived) to the frontend in real time — this is what actually
satisfies the BRD's "UI to illustrate how the passenger pickup and shuttle
system works" requirement ([brd-analysis.md §1](brd-analysis.md)) without
polling. REST was chosen over GraphQL/gRPC because the API surface is small
and fixed (5 endpoints), so GraphQL's flexibility is unneeded overhead, and
gRPC's main win (typed contracts across service boundaries) doesn't apply
inside one process.

### 1.5 Authentication and authorization strategy
**None**, per the confirmed scope
([brd-analysis.md §7.2](brd-analysis.md), [nfr-analysis.md §4](nfr-analysis.md)) —
a single implicit role, unrestricted access. HTTPS/TLS is still enforced end
to end (the one hard security target NFR-4 states regardless of the no-auth
decision, to protect the integrity of capacity/weight data in transit).
Input validation at the API boundary (unknown planet, non-positive
weight/count, origin==destination) is enforced via FluentValidation or data
annotations before a request ever reaches the Dispatch module.

### 1.6 Deployment model
Single multi-stage `Dockerfile`: stage 1 builds the frontend (`npm run build`
if React, or skipped if Blazor Server — see trade-off below) and the .NET
publish output; stage 2 is a minimal ASP.NET runtime image serving both the
API and the built static frontend assets from `wwwroot`. One
`docker run`/`docker-compose up`, SQLite file on a mounted volume for
persistence across container restarts (RTO < 5 minutes per
[nfr-analysis.md §1](nfr-analysis.md) — a restart just reopens the same file).
No external DB server, no cloud account — matches the Cost NFR's "$0, no
paid dependencies" line item exactly.

**Frontend choice within this option:** either React (clean separation,
larger ecosystem, more common interview-reviewer expectation) or Blazor
Server (all-C#, no JS build step, real-time UI comes free since Blazor Server
already holds a live SignalR-like connection — arguably an even tighter fit
for "illustrate the shuttle system live"). Both fit this option's deployment
model unchanged; React is assumed for the rest of this document as the more
broadly recognizable choice, with Blazor Server flagged as a valid swap.

### 1.7 Pros and cons

**Pros**
- Matches every Cost NFR target exactly ($0, single container, embedded DB).
- Simplest possible path to the Availability NFR's RTO (<5 min restart) and
  RPO (zero data loss) — no distributed-transaction or multi-node failure
  modes to reason about.
- Atomic capacity check-and-reserve (Edge Case #4) is a single in-process
  lock, not a distributed-locking problem — lowest risk of the "bug-free"
  goal being violated under concurrency.
- One codebase, one deploy pipeline, one thing to run locally — matches
  "running the server should be easy" literally.
- Module boundaries (via `Shared.Contracts`) keep a real extraction path open
  without paying for it now.

**Cons**
- All modules share one process/failure domain — a bug in `Modules.History`
  (e.g. a bad migration) can take down `Modules.Dispatch` too. Mitigated by
  keeping the in-memory fleet state authoritative for the hot path and
  history writes on a tolerant/retryable path (outbox-style), so a history
  write hiccup doesn't block dispatch.
- Scaling beyond one instance requires either accepting the in-memory fleet
  state becomes a bottleneck/single point of truth (fine at demo scale; not
  fine if this became a real multi-instance production deployment) or
  re-architecting fleet state into a shared store — a real limitation if the
  "future-proofed" goal is read as "must horizontally scale," not just
  "must be extensible."

### 1.8 NFR fit

| NFR category | Satisfied | Traded off |
|---|---|---|
| Availability | RTO <5min (fast container restart), RPO zero (durable write-through to SQLite before ack) | 99.5% ceiling assumes single instance — no failover if the one container/host dies outright |
| Performance | p50/p95/p99 dispatch latency targets easily met (in-memory decision, no network hop) | None significant at this scale |
| Scalability | Meets the actual stated targets (≥50 concurrent, 100k history rows, config-only fleet/planet growth) | Does not horizontally scale past one instance without further work — acceptable since NFR explicitly scopes to "single instance" |
| Security | HTTPS baseline, input validation at boundary, no-auth matches confirmed scope | None — this option doesn't add or remove anything from the confirmed security scope |
| Cost | Exact match: $0, self-hosted single container, embedded DB | None |

### 1.9 Team size and expertise requirements
**1-2 engineers**, comfortable with ASP.NET Core, EF Core, and basic
concurrency primitives (locks/semaphores). No distributed-systems, message
broker, or cloud-platform expertise needed. This matches a take-home
challenge's actual staffing (a single candidate).

### 1.10 Estimated complexity
**Low-to-Medium.** The domain logic (dispatch scoring, state machine,
capacity math) has real substance, but the infrastructure around it
(one web host, one embedded DB, no network topology) is about as simple as a
stateful system gets.

---

## 2. Option B — Microservices

### 2.1 Architecture style
The same four domains (Fleet, Dispatch, Planets, History) as independently
deployed .NET services behind a gateway, communicating over the network
instead of in-process calls.

### 2.2 Project structure and service boundaries

```
services/
├── gateway/                # YARP or Ocelot reverse proxy — single public entry point
├── fleet-service/           # Owns shuttle state; exposes gRPC for low-latency reads,
│                              REST for admin/config
├── dispatch-service/        # Calls fleet-service to read state, then must still perform
│                              an atomic reserve — see 2.7 for why this is the hard part
├── planets-service/         # Trivial CRUD/config service for the 5 planets
├── history-service/         # Owns TravelRequest/TravelHistory persistence + stats API;
│                              subscribes to a "TripCompleted"/"CallRejected" event stream
└── docker-compose.yml       # 5 app containers + message broker + per-service DB(s)
```

Each service owns its own datastore (SQLite per service, or a shared
Postgres instance if operational simplicity is prioritized over strict
per-service isolation). `history-service` is decoupled via an event bus
(RabbitMQ/MassTransit) so a slow or down history write never blocks dispatch
— which is the correct NFR-driven design instinct here, but note it
recreates, at network cost, exactly the separation Option A already gets for
free from its in-memory/durable-write split.

### 2.3 Database choice and schema approach
Polyglot-capable — SQLite per service works but is now 4-5 separate files to
manage instead of 1; a shared Postgres instance is more typical for a
microservices demo and avoids SQLite's single-writer limitation becoming a
per-service bottleneck. Either way, this is strictly more DB operational
surface than Business Rule 6 asks for ("no complex database systems").

### 2.4 API design approach
Mixed: **gRPC** between `dispatch-service` and `fleet-service` for the
latency-sensitive state read (protobuf, low overhead), **REST** at the
gateway for the frontend, and **events** (RabbitMQ) from `dispatch-service`/
`fleet-service` to `history-service` for the async persistence path.

### 2.5 Authentication and authorization strategy
Same no-auth conclusion at the edge (gateway), but now **service-to-service
auth** (mTLS or short-lived JWTs between services) becomes a real design
question the moment there's more than one process — even with "no user
auth," an internal network boundary now exists that Option A never has to
think about.

### 2.6 Deployment model
Docker Compose (or Kubernetes for anything beyond a laptop demo) orchestrating
5+ containers plus a message broker. This is a direct violation of the Cost
NFR's "self-hosted, single container" target and the "no complex DB setup"
business rule — not because microservices are wrong in general, but because
this specific domain (4 shuttles, 5 planets, demo scale) doesn't produce
enough independent scaling or team-ownership pressure to justify it.

### 2.7 Pros and cons

**Pros**
- Genuinely fits a future where Fleet/Dispatch/History are owned by separate
  teams or need to scale independently (e.g. history/analytics growing much
  faster than dispatch traffic).
- Failure isolation is real, not just structural — `history-service` being
  down cannot crash `dispatch-service`.

**Cons**
- The atomic capacity check-and-reserve (Edge Case #4) — trivial as one
  in-process lock in Option A — becomes a genuine distributed-systems
  problem: `dispatch-service` must reserve capacity in `fleet-service` with
  either a distributed lock, a compare-and-swap API on fleet-service, or a
  saga/compensation pattern if the reservation can partially fail. This is
  the single biggest risk to the "bug-free" NFR goal in this option —
  exactly the kind of complexity the BRD's fixed, tiny fleet (4 shuttles)
  doesn't warrant.
- Directly contradicts the Cost NFR (single container, $0, no complex DB) and
  Business Rule 6 (no complex database systems) as scoped for this challenge.
- Local dev/reviewer setup now requires `docker-compose up` with 5+ services
  and a broker instead of one container — actively works against "running
  the server should be easy."

### 2.8 NFR fit

| NFR category | Satisfied | Traded off |
|---|---|---|
| Availability | Per-service failure isolation | RTO gets *worse*, not better — more moving parts to restart/coordinate; harder to reason about which combination of services must be up |
| Performance | Can scale hot paths independently in a real deployment | Added network hops (gateway → dispatch → fleet) push latency budgets from §2 of the NFR doc closer to their p95/p99 ceilings for no benefit at this scale |
| Scalability | Best-in-class *if* traffic ever exceeded single-instance limits | Massive over-provisioning for the stated ≥50-concurrent-user, 4-shuttle target — solves a scaling problem this domain doesn't have |
| Security | Same no-user-auth scope | Adds a new internal attack surface (inter-service calls) that must now be secured even though nothing required it |
| Cost | — | Fails outright: requires multiple containers + broker, contradicting the $0/single-container target directly |

### 2.9 Team size and expertise requirements
**3-5+ engineers**, with distributed-systems experience: message brokers,
service-to-service auth, distributed tracing/observability (needed just to
debug a single call flow across 4 services), and container orchestration
beyond a single Dockerfile.

### 2.10 Estimated complexity
**High.** Most of the added complexity is pure operational/infrastructure
overhead — the domain logic itself doesn't get any easier or more correct by
splitting it up; if anything, the hardest correctness requirement (atomic
dispatch) gets strictly harder.

---

## 3. Option C — Serverless

### 3.1 Architecture style
Each capability as an independently deployed function (Azure Functions,
or the AWS Lambda equivalent): `CallShuttle`, `GetPlanets`, `GetHistoryStats`,
plus a stateful **Durable Function orchestrator/entity** to hold fleet state,
since plain stateless functions have nowhere durable-but-fast to keep the
shuttle manifests between invocations.

### 3.2 Project structure and boundaries

```
functions/
├── CallShuttle/              # HTTP-triggered; validates input, calls the FleetEntity
├── FleetEntity/               # Durable Entity — the only place shuttle state actually
│                                lives; serializes access to avoid the same overbooking
│                                risk as Option B, via the platform's built-in entity
│                                concurrency model instead of a hand-rolled lock
├── GetPlanets / GetHistoryStats/  # Simple HTTP-triggered reads
├── OnTripCompleted/            # Event-triggered (queue) — writes to storage, satisfies
│                                 the durable-history requirement
└── frontend/ (Static Web App)  # React SPA, calls the Function endpoints directly
```

### 3.3 Database choice and schema approach
Azure Table Storage or Cosmos DB (or DynamoDB on AWS) for `TravelRequests`/
history — a managed, schemaless key-value store, not the "no complex DB
setup" embedded file store the BRD asks for; it's actually less setup*
*for the provider*, but it is a cloud dependency the BRD's own constraint was
written to avoid, and it stops being "just run it" for a reviewer without a
cloud account.

### 3.4 API design approach
REST (HTTP-triggered functions) for calls/reads, plus **events** (a storage
queue or Event Grid) for the async `OnTripCompleted` history write — the
same async-persistence instinct as Option B, gained here for free from the
platform's trigger model rather than a hand-run broker.

### 3.5 Authentication and authorization strategy
Same no-auth conclusion for end users; function-level access keys would be
the platform-native way to lock down the HTTP endpoints if this were ever
exposed publicly, which is more than this scope requires but effectively
free to add.

### 3.6 Deployment model
Cloud-only (Azure Functions Consumption plan, or AWS Lambda + API Gateway).
Local development uses Azure Functions Core Tools / Azurite emulator, which
narrows but doesn't close the gap with "running the server should be easy" —
a reviewer still needs the Functions runtime installed, and a real deployment
needs a cloud account, directly contradicting the Cost NFR's "$0, no cloud
provider" target.

### 3.7 Pros and cons

**Pros**
- True scale-to-zero cost model if traffic is bursty or near-zero most of the
  time — philosophically the best fit for "pay for what you use," which is
  the spirit (if not the letter) of the Cost NFR.
- Durable Entities give a built-in, correct answer to "atomic state mutation"
  (Edge Case #4) without hand-rolling locks (Option A) or a distributed saga
  (Option B).

**Cons**
- Cold starts on the Consumption plan directly threaten the p50 < 100ms /
  p95 < 300ms dispatch latency targets in
  [nfr-analysis.md §2](nfr-analysis.md) — a cold `CallShuttle` invocation can
  take seconds, not milliseconds, unless a paid always-warm/Premium plan is
  used, which then also contradicts the $0 cost target.
- Requires a cloud account to run at all — the single clearest violation of
  the Cost NFR ("None — explicitly avoided for this challenge") and the
  README's own "running the server should be easy" instruction.
- A fixed 4-shuttle, 5-planet domain has none of the bursty/unpredictable
  traffic shape that makes serverless economically or operationally
  attractive; there is no idle-cost problem here for it to solve.

### 3.8 NFR fit

| NFR category | Satisfied | Traded off |
|---|---|---|
| Availability | Platform-managed uptime SLA can exceed 99.5% | RTO/RPO reasoning shifts to trusting the cloud provider's guarantees, which is fine in production but is a dependency this challenge's scope explicitly avoids introducing |
| Performance | — | Cold starts put the p50 dispatch-latency target at real risk without paid mitigation |
| Scalability | Best theoretical ceiling of all three options | Wildly exceeds what a fixed 4-shuttle domain will ever need |
| Security | Same no-user-auth scope; function keys available | Adds cloud IAM/identity surface to reason about that the other options don't have |
| Cost | Scale-to-zero is philosophically aligned | Fails the literal target: requires a cloud account/provider, which the Cost NFR explicitly rules out for this challenge |

### 3.9 Team size and expertise requirements
**1-2 engineers**, but specifically with cloud-native/serverless platform
experience (Durable Functions or Step Functions, managed NoSQL data
modeling, IAM) — a different skill set than Option A's, not a smaller one.

### 3.10 Estimated complexity
**Medium-High.** Individual functions are simple, but the state model
(Durable Entities) and the cloud account/IAM setup are non-trivial and
largely orthogonal to the actual business problem being solved.

---

## 4. Option D — Vertical Slice Architecture

**A note before the six dimensions:** unlike Options A/B/C, VSA is not an
infrastructure choice — it's a **code-organization style for a single
deployable**. It answers "how do we arrange the code inside the process,"
not "how many processes / where do they run." Concretely, that means Option
D's DB choice, auth strategy, and deployment model are **identical to Option
A's** — the only dimensions that actually differ are architecture style,
project structure, and (to a lesser extent) API design. It is scored here as
a full 4th option per the requested comparison, but the honest framing is
that VSA is a candidate *internal structure for Option A*, not a competing
peer of B or C the way A/B/C compete with each other.

### 4.1 Architecture style
Each use case ("slice") — `CallShuttle`, `GetPlanets`, `GetShuttleStatus`,
`GetHistoryStats` — owns its full vertical: request contract, validator,
handler, and response, with no shared generic Service/Repository layer in
between. A slice's handler talks directly to the same in-memory fleet state
and EF Core `DbContext` that Option A already defined; VSA changes how those
use cases are *packaged*, not what they call into. Still a single ASP.NET
Core process, same as Option A.

### 4.2 Project structure and module boundaries
Boundaries are drawn by **use case (verb)** instead of Option A's
**bounded context (noun)** grouping (Fleet/Dispatch/Planets/History):

```
src/
├── SpaceAcademy.Api/
│   ├── Features/
│   │   ├── CallShuttle/
│   │   │   ├── CallShuttleEndpoint.cs     # Minimal API route registration
│   │   │   ├── CallShuttleRequest.cs
│   │   │   ├── CallShuttleValidator.cs     # FluentValidation, scoped to this slice only
│   │   │   ├── CallShuttleHandler.cs       # orchestrates the use case; calls Shared/
│   │   │   └── CallShuttleResponse.cs
│   │   ├── GetPlanets/
│   │   ├── GetShuttleStatus/
│   │   └── GetHistoryStats/
│   ├── Shared/
│   │   ├── FleetState.cs                   # the SAME lock-guarded in-memory singleton
│   │   │                                      as Option A — deliberately NOT duplicated
│   │   │                                      per slice (see 4.7 for why this matters)
│   │   ├── ShuttleStateMachine.cs           # Idle/EnRoute/Arrived transitions — shared
│   │   └── AppDbContext.cs                 # EF Core / SQLite — shared, not duplicated
│   └── Program.cs
└── SpaceAcademy.Web/                        # unchanged from Option A
```

The critical design rule, and the one place this write-up diverges from a
"pure" VSA pitch: true cross-cutting domain invariants — the capacity caps,
the atomic reserve, the state machine — stay centralized in `Shared/`, not
duplicated per slice. A slice's handler is thin orchestration ("validate,
call FleetState.TryReserve(...), call the DbContext to persist") — VSA is
applied to *use-case orchestration*, not to the domain rules a "bug-free"
BRD goal can't afford to have implemented twice, slightly differently, in
two different features.

### 4.3 Database choice and schema approach
**Unchanged from Option A** — SQLite via EF Core, same schema
(`Planets`/`Shuttles`/`TravelRequests`/`TravelRequestPassengers`). VSA does
not touch this dimension.

### 4.4 API design approach
Same **REST + SignalR** combination as Option A, but each REST endpoint is
defined and registered inside its own slice folder (Minimal API's
`MapPost`/`MapGet` colocated with the handler) rather than grouped into a
controller per module. FastEndpoints is a viable library alternative to
hand-rolled Minimal API + MediatR if less boilerplate is wanted; either
choice is a convention decision, not an architectural one.

### 4.5 Authentication and authorization strategy
**Unchanged from Option A** — none, per the confirmed scope
([brd-analysis.md §7.2](brd-analysis.md)), same HTTPS baseline. One minor,
genuine upside: input validation is naturally scoped per-slice (each
validator only knows about its own request shape), which is a slightly
tighter default than one shared validation layer trying to cover every use
case, though the practical difference at this domain's size is small.

### 4.6 Deployment model
**Unchanged from Option A** — single multi-stage Dockerfile, one container,
SQLite file on a mounted volume. Nothing about organizing code as vertical
slices changes how the application ships or runs.

### 4.7 Pros and cons

**Pros**
- Fastest path to add or change one use case in isolation — touch one
  `Features/<UseCase>/` folder, not a module spanning several technical
  layers.
- Strong per-slice testability: a handler test exercises one use case
  end-to-end without mocking a layered service graph.
- Less generic abstraction (no speculative `IShuttleRepository`,
  `IShuttleService` interfaces built "for later") for a feature set this
  small (roughly 4-6 use cases total) — directly reduces boilerplate the BRD
  doesn't need.
- Onboarding: reading `Features/CallShuttle/` end to end shows the whole use
  case in one place, no layer-hopping.

**Cons**
- **Duplication risk is the real cost, and it's a "bug-free"-goal risk, not
  a style preference.** VSA's most common failure mode is re-implementing a
  shared invariant slightly differently in two slices — e.g. if a future
  "admin reassigns a shuttle" feature reimplements the capacity check instead
  of calling the same `FleetState.TryReserve`, the two code paths can drift
  and silently violate Edge Case #4's atomicity guarantee. This has to be a
  held discipline (everything touching shared state lives in `Shared/`,
  never per-slice), not something the folder structure itself enforces.
- **Weaker future-extraction story than Option A's noun-based module split.**
  Option A's Fleet/Dispatch/Planets/History boundaries map directly onto
  plausible future service boundaries (§2 of this document draws Option B's
  services along exactly those lines). VSA's verb-based slices
  (CallShuttle, GetHistoryStats) don't map onto service boundaries the same
  way — "which slices would extract together into a future microservice" is
  a less obvious question to answer. This is a real, if modest at this
  domain's size, trade-off against the "future-proofed" framing Option A
  relied on.
- With only a handful of use cases in this domain, VSA's headline benefit
  (avoiding a bloated shared service class as feature count grows into the
  dozens) has little surface area to prove itself on here — the case for it
  gets stronger as the endpoint count grows, not at 5.

### 4.8 NFR fit
Because the infrastructure is identical to Option A, four of the five NFR
categories are **unaffected** — satisfied or traded off exactly as Option A
already documented in §1.8. The table below states that plainly rather than
re-deriving it:

| NFR category | vs. Option A |
|---|---|
| Availability | Unchanged — same single container, same durable write-through |
| Performance | Unchanged — same in-memory dispatch path, same latency profile |
| Scalability | Unchanged — same single-instance ceiling and config-only growth path |
| Security | Unchanged — same no-auth scope; per-slice validation is a minor, non-NFR-driving upside |
| Cost | Unchanged — identical $0/single-container/embedded-DB posture |

The dimension VSA actually moves is **not** one of the five named NFR
categories — it's the BRD's top-level "bug-free" and "future-proofed" goals,
via the duplication risk and weaker extraction-seam trade-offs in §4.7 above.

### 4.9 Team size and expertise requirements
**1-2 engineers**, same as Option A, plus specific familiarity with the
vertical-slice/CQRS-lite convention (a naming/organization convention, not a
new technology) and, critically, the discipline to keep shared invariants
centralized rather than re-implemented per slice.

### 4.10 Estimated complexity
**Low** — identical infrastructure to Option A; the only delta is a
code-organization convention, not new infrastructure or new failure modes.

---

## 5. Side-by-side comparison

| Dimension | A — Modular Monolith | B — Microservices | C — Serverless | D — Vertical Slice |
|---|---|---|---|---|
| Architecture style | Single deployable, module boundaries in-process | Independently deployed services | Function-per-capability | Single deployable, use-case-per-slice |
| Atomic dispatch (Edge Case #4) | In-process lock — trivial | Distributed reserve across services — hard | Durable Entity — built-in, but cold-start risk | Same in-process lock as A, shared via `Shared/FleetState` — trivial, but only if not duplicated per slice |
| DB | SQLite, embedded, 1 file | Per-service DB or shared Postgres | Managed cloud NoSQL | Same as A — SQLite, embedded, 1 file |
| API style | REST + SignalR | REST + gRPC + events | REST (HTTP triggers) + events | Same as A — REST + SignalR, colocated per slice |
| Auth | None (confirmed scope) | None at edge, but service-to-service auth needed | None at edge, function keys available | Same as A — none |
| Deployment | 1 Docker container | 5+ containers + broker | Cloud account required | Same as A — 1 Docker container |
| Meets Cost NFR ($0, single container, no complex DB) | Yes, exactly | No | No | Yes, exactly (identical to A) |
| Meets Performance NFR (p50<100ms dispatch) | Yes, comfortably | At risk (extra network hops) | At risk (cold starts) | Yes, comfortably (identical to A) |
| Meets Availability NFR (RTO<5min, RPO zero) | Yes | Harder to reason about (more moving parts) | Depends on provider SLA, different risk profile | Yes (identical to A) |
| Future-extraction seam quality | Strong — noun-based modules map to plausible service boundaries | N/A — already extracted | N/A — already extracted | Weaker — verb-based slices don't map to service boundaries as cleanly |
| Team size | 1-2 | 3-5+ | 1-2 (different skillset) | 1-2 |
| Complexity | Low-Medium | High | Medium-High | Low |

---

## 6. Recommendation

**Recommend Option A — Modular Monolith.**

Every numeric target in `nfr-analysis.md` was derived from four scoping
decisions stated at its top: take-home challenge context, self-hosted single
container, demo-scale traffic, no compliance/auth regime
([nfr-analysis.md, opening table](nfr-analysis.md)). Option A (and, since it
shares Option A's infrastructure exactly, Option D) is the only one of the
four that satisfies all five NFR categories **as literally scoped**, not just
"could satisfy in principle":

- It hits the Cost NFR's $0/single-container/embedded-DB target exactly,
  which is also Business Rule 6 verbatim ("no complex database systems").
- Its in-memory fleet state + durable SQLite write-through design turns the
  hardest correctness requirement in the whole BRD — the atomic
  capacity-check-and-reserve (Edge Case #4) — into a single in-process lock,
  directly serving the "bug-free" top-level goal, whereas both alternatives
  either make that problem harder (Option B: distributed reservation) or
  trade it for a different platform-specific risk (Option C: cold starts
  undermining the latency budget the correctness check has to run inside of).
- The module-boundary discipline (`Shared.Contracts`, no cross-module
  references) is the concrete, low-cost answer to "scalable, future-proofed"
  that this domain's actual shape (4 shuttles, 5 planets, demo-scale traffic)
  calls for — it keeps a real extraction path open (to Option B's shape,
  later, if actual production load ever demanded it) without paying
  distributed-systems tax today.
- It matches the team-size reality of this engagement: 1-2 engineers with
  mainstream ASP.NET Core skills, not a platform team.

Options B and C aren't "wrong" in a vacuum — they're the correct answer to
problems this domain doesn't have yet (independent team ownership at scale,
or genuinely bursty/idle-heavy traffic). Including them here mainly documents
*why* they were considered and consciously not chosen, which is the same
judgment call the BRD itself asked the candidate to make explicit
("please make any assumptions you see fit and just call it out,"
[README.md](../README.md)). If this system ever moved from challenge
submission to a real production rollout — the same trigger condition
`nfr-analysis.md` already names for revisiting every NFR target — Option B's
service boundaries (already drawn along the same module lines as Option A)
would be the natural next step, extracted one module at a time rather than
as a rewrite.

### 6.1 Where Option D (Vertical Slice Architecture) fits into this

Option D isn't a real alternative *to* Option A the way B and C are — §4's
opening note already says so, and the comparison table in §5 shows every
infrastructure-facing row (DB, auth, deployment, all three satisfiable NFR
categories) as identical between A and D. The actual decision on the table
is narrower than "A or D": it's **how Option A's internals should be
organized** — by bounded-context module (A, as originally specified) or by
use-case slice (D).

**Concrete recommendation: adopt a hybrid, not a pure version of either.**
Keep Option A's four bounded contexts as the top-level grouping (`Fleet`,
`Dispatch`, `Planets`, `History`), and organize the *use cases within each*
as vertical slices:

```
src/SpaceAcademy.Api/
├── Dispatch/
│   ├── Features/
│   │   └── CallShuttle/          # slice: request, validator, handler, response
│   └── Shared/
│       └── FleetState.cs          # the one, non-duplicated, lock-guarded source of
│                                    truth for capacity + the state machine
├── History/
│   └── Features/
│       └── GetHistoryStats/
├── Planets/
│   └── Features/
│       └── GetPlanets/
└── Fleet/
    └── Features/
        └── GetShuttleStatus/
```

This gets Option D's real, genuine wins — thin, testable, single-purpose
use-case handlers with minimal generic-layer boilerplate for a domain that
only has a handful of use cases — without its real cost: the noun-based
module boundary is preserved, so the "future-proofed" extraction-seam story
from §1.2 stays intact (a bounded context, not a scattered set of verbs, is
still the unit you'd lift into a service later), and the one invariant that
must never be duplicated — the atomic capacity check-and-reserve — has one
obvious home (`Dispatch/Shared/FleetState.cs`) rather than an ambiguous one.
In short: **use Option A's module map to answer "where does this code
live," and Option D's slice discipline to answer "how is a single use case
inside that module structured."** They were never actually competing
answers to the same question.
