# Assumptions

The brief says: *"Please make any assumptions that you see fit and just call it
out."* This is that call-out. Every item below is a decision the brief left open,
what was chosen, and where it lives in the code.

Numbering follows §7 of [`brd-analysis.md`](brd-analysis.md).

---

## Scope and access

**1–2. No roles, no authentication.**
The brief names no users, accounts, or permissions. Every endpoint is anonymous and
a passenger is described only by species and weight, with no identity carried
between calls. *If this went further:* the analyst persona implied by "help the team
to study most traveled planets" is the first thing that would need a real role.

**8. History is an API, not a screen.**
`GET /api/travel-history/stats` answers "which planets are most travelled" directly.
No admin UI was built — the brief asks for the data, and a front end is out of scope
for this backend.

## The call

**3. One call is one party.**
A `TravelRequest` carries one or more life forms travelling together to a single
destination. `Features/CallShuttle/`, `Domain/TravelRequest.cs`.

**4. Species is a label; weight is the number that matters.**
Species is free text with no effect on capacity maths — the brief says "not just for
the human race" but defines no species catalogue. Weight is required and must be
greater than zero. `Domain/LifeForm.cs`.

**Life-form count and total weight are derived, never stored.**
`TravelRequest.LifeFormCount` and `TotalWeightKg` are computed from the party, so
they cannot drift out of step with the manifest. This is why the dual capacity check
can be trusted.

**5. Legs are one-way.**
"Travel from and to a planet" is read as one leg from your dock to a chosen planet.
A return trip is a new call from the destination dock.

**11. Origin must differ from destination.**
Enforced twice: in `CallShuttleValidator` (returns 400) and in
`TravelRequest.Create` (throws), so the rule holds even if the domain is used
directly.

## Capacity

**The two caps are checked jointly.**
The brief's "20 life forms OR maximum load of 4000kg" means *whichever is reached
first*, so a party only fits when it clears both ceilings. Four life forms at 3900 kg
leave sixteen free seats but only 100 kg — a 150 kg fifth passenger does not fit.
`Domain/Capacity.cs`; proved in `CapacityTests`.

**10. A party is never split across shuttles.**
It travels whole or waits. `Domain/Fleet.cs`, Rule 1/2 both test the party as a unit.

**A party no shuttle could ever carry is rejected immediately, not queued.**
21 life forms, or a single 5000 kg life form, would wait forever. It fails fast with
a reason naming the actual limits.

**9. Fleet size, planet list and capacity are configuration.**
The brief says "scalable, future proofed", which sits badly with hardcoded numbers.
All of it lives in `appsettings.json`; the code contains no `20`, `4000`, `4` or `5`
literals. Adding a fifth shuttle is a config edit.

## Dispatch — the "smarts"

**7. What "efficient" optimises.** The brief says shuttles burn a lot of fuel and
should be smart about pickups, but defines no cost function and gives no numeric
distances — only an ordering. The implemented policy, as an ordered rule list in
`Domain/Fleet.cs`:

1. **Reject** what no shuttle could ever carry.
2. **Batch** onto a shuttle already committed to this exact origin *and* destination
   that has room — one launch instead of two is the largest fuel saving available.
3. **Nearest idle** shuttle, by distance rank to the calling dock.
4. **Queue**, FIFO.

Ordinal planet rank is the distance and fuel proxy throughout, because it is the only
distance data that exists.

**Batching needs no artificial "hold and fill" rule.**
`Dispatch` commits a shuttle but never moves it; only the simulation tick launches.
So a shuttle sits collectable at the dock until the next tick, and a shuttle flying
in empty to collect is collectable for its whole approach. Both are natural windows
for a second party to join, so no deliberate departure delay was invented.

**A shuttle's manifest is homogeneous — one origin, one destination.**
No multi-stop routing. The brief never asks for it, and it keeps "is this shuttle
going my way?" a single comparison. *If this went further:* multi-stop pickup is the
obvious next fuel saving, and Rule 2 is where it would go.

**6. Nearest-idle can beat fuel saving.**
When the nearest shuttle is far away, it is still sent rather than making the party
wait for a closer one to free up. The rules are an ordered, readable list precisely
so this trade-off is tunable rather than buried.

**13. When every shuttle is busy, the call queues.**
It is re-dispatched automatically as shuttles free up — no polling by the caller, no
rejection.

## Time

**Travel takes time, proportional to distance.**
`max(1, |rank difference|) × 3 seconds`, configurable. Without a duration model,
distance would be cosmetic and every dispatch decision would be equivalent — there
would be nothing for the "smarts" to optimise. A `BackgroundService` ticks every
500 ms and is the only thing that moves a shuttle.

**Three shuttle states**, per the brief's implied lifecycle: `Idle`, `EnRoute`,
`Arrived`. Whether an en-route shuttle is loaded or repositioning empty is a property
of its manifest, not a fourth state.

## Persistence

**12. Rejected calls are recorded alongside completed trips.**
This is deliberate and arguably the most useful data the system holds: a dock that
keeps turning parties away is precisely where the next shuttle should be deployed —
the brief's stated reason for persisting anything. `GET /api/travel-history/stats`
reports `completedTrips` and `rejectedCalls` per planet, and lists planets with zero
of both, because a zero is a finding.

**A completed request keeps a permanent record of which shuttle flew it.**
`AssignedShuttleId` is set on assignment and never cleared. (An earlier version
modelled the manifest as an EF relationship, and unloading a party silently nulled
this — the manifest is now rebuilt by `FleetRepository` instead.)

**15. Durability without an outbox.**
A trip's completion and its history row are written in one `SaveChanges`, so a trip
cannot be delivered without being recorded. In a single-process monolith with a local
store, this gets the guarantee an outbox would buy, with none of the machinery.

**SQLite, one file, migrated and seeded at startup.**
The brief asks that "running the server should be easy (no need for complex database
systems)". `dotnet run` is the whole setup.

**Weights are stored as `REAL`.** EF maps `decimal` to SQLite `TEXT` by default,
which silently breaks `SUM` and `ORDER BY` — it would have quietly corrupted the
stats. Kilograms to three decimal places are far inside double's exact range.

## Concurrency

**14. Dispatch is serialised by a single in-process lock.**
`Code/Dispatch/DispatchGate.cs` — a `SemaphoreSlim(1,1)` held across the whole
load → decide → save, by both the API handler and the simulation tick. Two
simultaneous calls therefore cannot both claim the last seat.

**This is the one thing that blocks horizontal scale-out, and it is deliberate.**
For a single-process monolith over a single-writer SQLite file it is correct and
nothing is lost. It sits behind `IDispatchGate` so it can be swapped for a
distributed lock without touching `Domain/` or `Features/`.

**16. No de-duplication of repeat calls.**
Each call is treated as a legitimate independent request. The brief does not raise
it, and guessing at a de-dup window would invent a rule nobody asked for.

---

## Known limits

Stated plainly rather than left to be discovered:

- **Single process only** — see the dispatch gate above.
- **Simulated travel time** is a stand-in. The brief gives an ordering of planets and
  nothing else: no distances, speeds, or fuel figures.
- **No multi-stop routing** — a shuttle serves one origin and one destination per trip.
- **No authentication**, so anyone can call a shuttle or read the history.
- **The batching window at a dock is one tick (~500 ms).** Parties batch reliably onto
  a shuttle flying in to collect them (a window of seconds), but two walk-up calls at
  the same dock only share a shuttle if they arrive within the same tick. Widening
  this would mean a deliberate boarding delay — a rule the brief does not ask for.
