# BRD Analysis — OVO Space Frontend Coding Challenge

**Source document:** [`README.md`](../README.md)
**Analysis type:** Senior BA teardown — roles, entities, workflows, business rules, edge cases, and open questions — to be resolved before an architecture/implementation plan is written.

**How to read this document:** every gap in the BRD is marked inline with
`[CLARIFICATION NEEDED]` at the point it surfaces, with a suggested default
assumption underneath it (per the BRD's own instruction: *"please make any
assumptions that you see fit and just call it out"*). Section 7 repeats every
flag in one flat, scannable list.

---

## 1. Overview

The BRD describes one feature — a **space travel booking and dispatch system**
— split into a front-end and a back-end requirement set:

- A fixed fleet of **4 space shuttles** serves **5 known planets** (Angel 1,
  Boreth, Aurelia, Blue Horizon, Argus X — listed in increasing distance from
  the space academy/home base).
- Each planet has its own **space dock**, the point of presence where a
  **passenger** (explicitly "life forms," not only humans) requests travel.
- Shuttles have a **dual capacity limit**: 20 life forms *or* 4000kg of load,
  whichever is reached first.
- The system should dispatch shuttles "smartly" to conserve fuel (liquid
  oxygen/hydrogen).
- All travel activity must be **persisted via an API** so the team can later
  analyze the most-traveled planets and plan fleet expansion.
- Non-functional goals stated up front: **bug-free, scalable, future-proofed,
  resilient**, and the backend must be **simple to run** (no complex DB setup).

This is a single feature with two delivery surfaces (Front End, Back End); the
analysis below treats them together since they share the same domain model.

---

## 2. User Roles & Permissions

| Role | Source in BRD | Permissions / Actions |
|---|---|---|
| **Passenger (Life Form)** | "Passenger can call a shuttle whenever they need to travel from and to a planet via the space dock" | Select a destination planet at a space dock; call/request a shuttle; (implicitly) board an assigned shuttle. No stated restriction on who may call — any life form, any dock, any time. |
| **Dispatch System** | Implied by "smarts to be efficient when picking up passengers" | Not a human role — the system itself decides which of the 4 shuttles serves a given call. This is an automated actor, not a user role, but it has effectively full authority over shuttle assignment with no override path described. |
| **Space Academy Team / Analyst** | "This will help the **team** to study most traveled planets and deploy more shuttles in the future" | Implied consumer of travel history data. **No interface, endpoint, or permission model for this role is specified anywhere in the BRD.** |

**`[CLARIFICATION NEEDED]` — Is there an Admin/Operator role?**
The BRD's stated purpose for persisting history ("help the team study... and
deploy more shuttles") implies someone views aggregated stats, but no role,
screen, or endpoint is defined for it, and no permission model (e.g., can a
passenger see other passengers' travel history? Is history public or
restricted?) is given at all.
*Assumption: treat this as a single-role, no-auth system for this challenge —
one implicit "Passenger" role with unrestricted access to call shuttles and
read aggregate (not personally-identifying) history/stats via a simple
GET endpoint or basic dashboard. No login/auth is in scope.*

**`[CLARIFICATION NEEDED]` — Is authentication/identity in scope at all?**
The BRD never mentions accounts, login, or identifying a specific passenger
across trips.
*Assumption: no authentication. A "passenger" in a travel request is described
by species + count/weight only, not a persistent identity.*

---

## 3. Core Entities & Relationships

| Entity | Key Attributes (as derivable from BRD) | Notes |
|---|---|---|
| **Planet** | name, distance rank (Angel 1 = nearest ... Argus X = farthest) | Fixed set of 5; distance is ordinal only — no numeric distance/time/fuel-cost value is given. |
| **SpaceDock** | 1:1 with Planet | "Every planet to have its own space dock." Acts as the pickup location *and* the origin from which a call for the *next* planet is made. |
| **Shuttle** | id, capacity (20 life forms **or** 4000kg), current location/status | Fixed fleet size of 4. No stated attribute for fuel level, speed, or travel-time-per-leg. |
| **LifeForm (Passenger)** | species, weight | Only two attributes are ever implied. Species is mentioned only as "not just the human race" — no enumerated list, no per-species weight/behavior rules given. |
| **TravelRequest ("Call")** | origin dock (planet), destination planet, life form(s) + weight(s), timestamp, assigned shuttle, status | The unit of work created when a passenger "calls a shuttle." |
| **TravelHistory record** | derived from completed/persisted TravelRequests | "Persist travel data history via api" — read model for analytics (most-traveled planets, etc.). |

### Relationships
- Planet **1 — 1** SpaceDock.
- Planet **1 — many** TravelRequest (as destination); a dock is also the
  *origin* of a request, so Planet is referenced twice per request (from/to).
- Shuttle **1 — many** TravelRequest (over time, sequentially — a shuttle
  serves one request/trip at a time).
- TravelRequest **1 — many** LifeForm (a call can represent a group, subject to
  the capacity cap) — **see clarification below**.
- TravelRequest **1 — 1** TravelHistory record (a completed request becomes a
  permanent history entry).

**`[CLARIFICATION NEEDED]` — Does one "call" represent one life form or a group?**
"Passenger can call a shuttle" (singular) vs. the capacity being expressed in
life-forms-per-shuttle (up to 20) suggests shuttles are meant to be shared/
batched across multiple calls, but it's not stated whether a single call can
itself request multiple life forms at once (e.g., a family group).
*Assumption: a single call is for one or more life forms travelling together
to the same destination (a "party"), with a combined weight; the shuttle's
capacity check applies to the sum of all life forms currently aboard,
accumulated across one or more calls it has picked up.*

**`[CLARIFICATION NEEDED]` — What defines a life form's "weight" and are there per-species defaults?**
No weight ranges, units validation, or species catalog is given.
*Assumption: weight is a required positive number in kilograms supplied per
call (or per life form in the call); the system does not need a canonical
species table for this challenge — species is a free-text/enum label used for
display and future extensibility only, not for weight calculation.*

**`[CLARIFICATION NEEDED]` — Is a "trip" one-way, or does "travel from and to a planet" imply a round trip?**
The requirement phrase is "travel **from and to** a planet via the space
dock," which can be read either as (a) travel from your current dock *to* a
chosen destination planet (i.e., "from [dock] to [planet]"), or (b) an implied
round trip (there and back).
*Assumption: reading (a) — each call is a single one-way leg, from the
passenger's current planet's dock to a chosen destination planet. A return
trip is simply a new call made from the destination's dock later.*

### 3a. Shuttle State Machine

The BRD never defines shuttle state explicitly — §4.2 below only names it
informally ("status: idle/en route/full"). Formalized here as exactly three
states, confirmed with the user:

| State | Meaning |
|---|---|
| **Idle** | Shuttle is stationed at a planet (its current location) and available for dispatch. |
| **En Route** | Shuttle is traveling toward a `destinationPlanet`. Carries a passenger manifest that may be **empty** (repositioning to reach a call's origin dock, if it wasn't already there) or **loaded** (carrying life forms to their destination) — this is an attribute of the state, not a separate state, so the model stays at 3 states total. |
| **Arrived** | Shuttle has reached `destinationPlanet`. Triggers unload (if loaded) and/or pickup (if this was a repositioning leg), plus trip-completion persistence to history, then immediately re-evaluates to `Idle` or straight back to `En Route` if a call is already queued at that planet. |

**Transition table:**

| From | Trigger | To |
|---|---|---|
| `Idle` | Dispatcher assigns a `TravelRequest` to this shuttle | `En Route` — destination is the call's origin planet if the shuttle isn't already stationed there (empty leg), otherwise directly the call's destination planet with passengers loaded immediately (loaded leg). |
| `En Route` | Simulated travel duration elapses | `Arrived` |
| `Arrived` | Unload/pickup processed + trip-completion persisted to history (system-internal, not user-facing) | `Idle`, or immediately back to `En Route` if a queued call at that planet is assigned right away |

**Travel duration (resolved with user):** En Route is **time-based**, not an
instant flip — a shuttle occupies En Route for a simulated duration
proportional to the destination's distance rank (Angel 1 shortest ... Argus X
longest; e.g. duration = rank index × a fixed base unit — the concrete
unit/value is an implementation detail, not a business rule). This is what
gives the BRD's "efficient pickup" requirement something real to optimize: a
shuttle en route to Argus X is genuinely unavailable for a meaningful stretch
of time, so which shuttle the dispatcher picks (assumption #7 in §7) actually
matters. Without a duration model, distance would be cosmetic and every
assignment would be equivalent.

This directly determines the data the dispatch algorithm (§4.2) reads to pick
a shuttle (current state, and if `En Route`, remaining time-to-`Arrived` and
current manifest/capacity), and it sharpens edge case #4 in §6: "atomic
assignment" specifically means atomically flipping a shuttle from
`Idle`/`Arrived` to `En Route` plus reserving its manifest capacity in one
step, so two concurrent calls can never both claim the same shuttle slot.

---

## 4. Complete User Workflows

### 4.1 Passenger calls a shuttle
1. Life form arrives at (or is represented at) the space dock of their current
   planet.
2. Passenger selects a destination planet from the other 4 (front-end
   requirement: "every planet to have its own space dock where a passenger
   selects which planet to go to").
3. Passenger (implicitly) supplies their life form details — species and
   weight — and submits the call. `[CLARIFICATION NEEDED]`: **is this data
   entered by the passenger, or assumed/pre-populated?** *Assumption: the UI
   collects species + weight (and quantity, if group calls are allowed) as
   part of the call form.*
4. System validates the request (see §5 Business Rules) and creates a
   `TravelRequest`.
5. System runs the dispatch/assignment logic (§4.2) to pick a shuttle.
6. Passenger sees the assigned shuttle (and, per the UI requirement, a visual
   of the pickup/shuttle system) or a wait/queued state if none is
   immediately available.
7. On shuttle arrival and boarding (out of scope for animation, per BRD), the
   trip is marked in-progress, then completed on arrival at the destination.
8. Completed trip is persisted to travel history via the backend API.

### 4.2 Shuttle dispatch ("smart," fuel-efficient pickup)
1. A new call arrives with an origin dock and destination planet.
2. System evaluates the 4 shuttles' current state (location, remaining
   capacity, status: idle/en route/full) to choose the most efficient one to
   serve the call.
3. Assign the call to the chosen shuttle; update its manifest and remaining
   capacity.
4. If no shuttle can serve the call at all (see §6 Edge Cases), handle
   according to the fallback rule.

**`[CLARIFICATION NEEDED]` — What does "efficient" actually optimize for?**
The BRD only says shuttles "use a lot of liquid oxygen and hydrogen for fuel...
best to have the smarts to be efficient when picking up passengers" — it does
not define the cost function. Candidate interpretations: (a) minimize total
distance/detour driven by the fleet, (b) prefer the nearest idle shuttle to
the calling dock, (c) prefer a shuttle already en route in a compatible
direction that has spare capacity (batch multiple calls into one trip), or
(d) minimize the number of shuttles dispatched (maximize load factor before
launching).
*Assumption: dispatch prefers, in order: (1) a shuttle already en route to the
same destination with spare capacity, then (2) the nearest idle shuttle to the
calling dock, using the given planet distance ordering as a proxy for
distance/fuel cost since no numeric distances are provided.*

### 4.3 Viewing travel history / stats
`[CLARIFICATION NEEDED]` — no workflow is described for *how* the "team"
studies most-traveled planets: is this a UI screen, a raw API response, an
exported report? *Assumption: out of full-UI scope for this challenge; a
simple read API (e.g., counts of trips per destination planet) satisfies the
stated business need, optionally surfaced in a minimal admin view.*

---

## 5. Business Rules & Validation Constraints

1. **Fleet size is fixed at 4 shuttles.** No requirement to add/remove
   shuttles at runtime is stated.
   `[CLARIFICATION NEEDED]`: the BRD's non-functional goal of "scalable,
   future proofed" seems to be in tension with a hardcoded fleet of 4 — is the
   fleet size expected to be configurable even though only 4 exist today?
   *Assumption: fleet size should be a configurable value defaulting to 4, not
   a hardcoded magic number, so the system is future-proofed without changing
   the current stated behavior.*
2. **Planet set is fixed at 5**, with a defined distance order (Angel 1
   nearest → Argus X farthest). Same future-proofing tension applies.
   *Assumption: planet list is configurable/extensible, defaulting to the 5
   named planets in the given order.*
3. **Capacity rule:** a shuttle may carry **up to 20 life forms, OR up to
   4000kg total, whichever limit is reached first** — i.e., a logical AND of
   two independent ceilings (a shuttle is full when *either* cap is hit).
   `[CLARIFICATION NEEDED]`: what happens when a call would push a shuttle
   over *one* of the two limits but not the other (e.g., shuttle has 3 life
   forms aboard totalling 3900kg — a 4th life form weighing 150kg would
   breach the weight cap at only 4 of 20 seats used)? Is the call rejected
   outright, queued for the next shuttle, or partially split?
   *Assumption: a call is only assigned to a shuttle if it fits within
   **both** remaining caps as a whole (the call's life-form count and total
   weight combined must fit); a call is never split across shuttles.*
4. **All calls must specify a valid destination** that is one of the 5 known
   planets and is not the passenger's current planet (a call cannot be "to"
   the planet you're already at).
   `[CLARIFICATION NEEDED]`: is this restriction actually intended, or could
   a same-planet "call" be a valid no-op / local hop? *Assumption: origin ≠
   destination is enforced; same-planet requests are rejected as invalid.*
5. **Every completed (and, arguably, every attempted) trip must be persisted**
   via the backend API for historical analysis.
   `[CLARIFICATION NEEDED]`: are *rejected* or *failed* call attempts also
   recorded (useful for capacity-planning "we needed a 5th shuttle here"), or
   only successfully completed trips? *Assumption: successful trips are
   always persisted; rejected/failed attempts are also logged (with a status
   flag) since that data directly serves the stated business goal of
   deciding where to deploy more shuttles.*
6. **Backend must run without a complex database** — i.e., no requirement for
   a full RDBMS/cluster; an embedded or file-based store is acceptable and
   preferred for ease of running the project.

---

## 6. Edge Cases & Error Scenarios

| # | Scenario | Question / Risk | Suggested Handling |
|---|---|---|---|
| 1 | All 4 shuttles are busy/at capacity when a new call comes in | `[CLARIFICATION NEEDED]`: queue, reject, or wait-and-retry? BRD is silent. | *Assumption:* queue the call (FIFO per dock, or globally) and auto-assign as soon as a shuttle frees up or is dispatched; surface a "waiting for shuttle" state to the UI. |
| 2 | A single life form's weight alone exceeds 4000kg | Never explicitly excluded by the BRD ("various life forms," not just humans, implies wide weight variance). | *Assumption:* reject the call with a clear validation error — no shuttle can ever satisfy it, so it should fail fast rather than queue forever. |
| 3 | 20 life forms requested but their combined weight would be well under 4000kg (or vice versa: few life forms but heavy) | Confirms the dual-cap rule must be checked jointly, not just count. | Both caps must be validated on every assignment attempt (see Business Rule 3). |
| 4 | Multiple docks call simultaneously (concurrency) | Two calls could be assigned to the same shuttle if capacity checks aren't atomic. | Dispatch/assignment must be a single atomic operation (e.g., a lock or transactional check-and-reserve) to prevent overbooking a shuttle past its cap. |
| 5 | A call supplies zero or negative life forms, or zero/negative/missing weight | Input validation gap not addressed in BRD. | *Assumption:* reject with a validation error; life form count ≥ 1, weight > 0 required. |
| 6 | The "efficient" shuttle is currently at the farthest planet (Argus X) when the nearest dock (Angel 1) calls | Tests whether "efficiency" ever trades off passenger wait time against fuel — BRD doesn't resolve this trade-off. | *Assumption:* documented as a known trade-off; default dispatch (§4.2) accepts a longer wait if it meaningfully saves fuel/distance versus sending an idle-but-farther shuttle — flagged as a tunable rule, not hardcoded. |
| 7 | Backend/API is temporarily unavailable when a trip completes | No resilience/retry requirement is stated despite "resilient" being a top-level goal. | *Assumption:* the persistence write should be retried/durable (e.g., write-ahead log or outbox pattern) so a completed trip is never silently lost — reasonable minimum given the stated "resilient" NFR. |
| 8 | Duplicate/rapid repeat calls from the same dock (double-submit) | Not addressed. | *Assumption:* de-duplicate identical in-flight requests from the same dock within a short window, or simply allow it (each call is a legitimate independent request) — treated as low-risk for this challenge; document the assumption rather than over-engineer. |
| 9 | An unknown/invalid planet name is submitted as origin or destination | Not addressed. | Reject with a validation error; only the 5 configured planets are valid. |

---

## 7. Consolidated Ambiguities & Missing Requirements — `[CLARIFICATION NEEDED]`

All flags from the sections above, in one place:

1. **Admin/analyst role** — Is there any role beyond "passenger," e.g. to view
   travel-history stats? No UI/endpoint/permissions are defined for the
   BRD's own stated purpose ("help the team study most traveled planets").
   *Default assumption:* single implicit role, no auth; a simple read
   endpoint/view exposes aggregate stats to anyone.
2. **Authentication/identity** — Is login/identity in scope at all?
   *Default assumption:* no auth; passengers are anonymous per-call.
3. **Group calls** — Does one "call" represent a single life form or a group
   travelling together?
   *Default assumption:* one call = one party (1+ life forms) to one
   destination, with a combined weight validated as a whole.
4. **Life form weight/species model** — No weight ranges, units, or species
   catalog are defined.
   *Default assumption:* weight is a required positive number in kg supplied
   per call; species is a free-text/enum label with no effect on capacity
   math for this challenge.
5. **One-way vs. round trip** — Does "travel from and to a planet" imply a
   return leg per call?
   *Default assumption:* one-way per call; a return trip is a separate call
   made later from the destination dock.
6. **Data entry mechanism** — Is passenger/life-form data manually entered
   per call, or otherwise sourced?
   *Default assumption:* entered via the call form at the space dock UI.
7. **Dispatch efficiency metric** — What exactly does "efficient pickup"
   optimize (distance, fuel, wait time, batching)? No cost function or
   numeric distances are given.
   *Default assumption:* prefer an already-en-route shuttle with spare
   capacity heading the same way, else the nearest idle shuttle, using the
   given ordinal planet distances as the fuel/distance proxy.
8. **History/stats presentation** — Is a UI required for viewing travel
   history, or is a raw API sufficient?
   *Default assumption:* a simple read API (trip counts per destination) is
   sufficient; a minimal view is a nice-to-have, not required.
9. **Fleet/planet configurability** — Given the "scalable, future-proofed"
   NFR, should the fixed counts (4 shuttles, 5 planets) be configurable
   rather than hardcoded?
   *Default assumption:* yes — model both as configurable collections
   defaulting to the stated values, without adding UI/features to actually
   change them for this challenge.
10. **Partial-capacity/split behavior** — When a call would breach one of the
    two capacity caps (count or weight) but not the other, is it rejected,
    queued, or split across shuttles?
    *Default assumption:* never split a single call across shuttles; a call
    is only assigned if it fits within both remaining caps together.
11. **Same-planet call validity** — Is a call whose destination equals the
    passenger's current planet meaningful, or invalid?
    *Default assumption:* rejected as invalid (origin must differ from
    destination).
12. **Failed/rejected attempt logging** — Does travel history capture only
    successful trips, or also failed/rejected call attempts (arguably more
    useful for the stated "deploy more shuttles" business goal)?
    *Default assumption:* log both, with a status field distinguishing them.
13. **No-shuttle-available fallback** — Queue, reject, or wait-and-retry when
    all 4 shuttles are busy/full?
    *Default assumption:* queue and auto-assign as capacity frees up.
14. **Concurrency/atomicity of dispatch** — Not addressed; needed to prevent
    two simultaneous calls from over-booking the same shuttle.
    *Default assumption:* assignment + capacity reservation must be atomic.
15. **Resilience of persistence** — The BRD names "resilient" as a top-level
    goal but gives no specifics for what happens if the API/store is
    unavailable when a trip completes.
    *Default assumption:* durable/retryable write path so a completed trip
    is never silently lost.
16. **Duplicate/rapid repeat calls** — Not addressed; low-risk, documented
    rather than solved for this challenge.
    *Default assumption:* no special de-duplication; each call stands alone.

---

## Summary

The BRD is intentionally lightweight (it is a take-home coding challenge, not
a production BRD), and its two explicit instructions — "make any assumptions
you see fit and call it out" and keep the backend simple to run — signal that
some ambiguity is expected to be resolved by the candidate's judgment rather
than by a client. The 16 items in Section 7 are the concrete decisions that
should be confirmed (or consciously assumed, as drafted above) **before**
moving into an architecture/implementation plan, since several of them
(capacity-check semantics, dispatch cost function, group-call model, and
fleet/planet configurability) directly shape the core domain model and API
contract.
