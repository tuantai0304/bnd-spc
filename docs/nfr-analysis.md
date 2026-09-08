# NFR Analysis — OVO Space Frontend Coding Challenge

**Source documents:** [`brd-analysis.md`](brd-analysis.md), [`README.md`](../README.md)
**Analysis type:** Non-functional requirements derivation — for each category, what
the BRD actually states (usually nothing numeric) vs. a specific, measurable
target proposed to fill the gap, with the reasoning behind each number.

**Scoping assumptions confirmed for this analysis** (via direct Q&A, not
inferred):

| Assumption | Value |
|---|---|
| Deployment context | Take-home **challenge submission** — targets are sized for a demo/portfolio deployment, not a funded production rollout. Where a real launch would need something different, that's called out separately. |
| Hosting | **Self-hosted, single container** + embedded/file-based DB — directly matches the BRD's own constraint ("simple to run... no complex DB setup," [brd-analysis.md §5.6](brd-analysis.md)). |
| Scale target | **Demo scale** — tens of concurrent callers, single instance. Not designed against hundreds/thousands of concurrent users. |
| Compliance | **None applicable** — no auth/identity in scope, no PII collected ([brd-analysis.md §7.2, §7.4](brd-analysis.md)). |

These four decisions gate every target below; if any of them changes (e.g. this
becomes a real production deployment), the numbers in this document need to be
revisited, not just the prose.

---

## 1. Availability

**What the BRD says:** "Resilient" is named as a top-level non-functional goal
([brd-analysis.md §1](brd-analysis.md)), and Edge Case #7 flags that a
completed trip must survive a temporarily unavailable backend/API. No uptime
number, SLA, or downtime budget is given anywhere.

| Requirement | Target | Reasoning |
|---|---|---|
| Uptime SLA | **99.5%** measured monthly | A single self-hosted container with an embedded DB cannot honestly promise 99.9%+ (that needs redundancy/failover this architecture doesn't have). 99.5% is the honest ceiling for a single-instance demo deployment while still being a real, stated commitment rather than "best effort." |
| Acceptable downtime/month | **≤ 3h 39m** (derived from 99.5%) | Direct arithmetic from the SLA above (0.5% of 30 days). Covers planned restarts/deploys plus unplanned crashes. |
| Recovery Time Objective (RTO) | **< 5 minutes** to restart and resume serving calls | Single-container deployments restart fast (no cluster coordination); 5 minutes covers container restart + health-check pass, and is achievable with a basic process supervisor/orchestrator restart policy. |
| Recovery Point Objective (RPO) | **Zero data loss for completed trips** | Directly required by the BRD's "resilient" NFR + Edge Case #7's durable-write assumption ([brd-analysis.md §6, row 7](brd-analysis.md)). A completed trip must be written durably (e.g. write-ahead log / outbox pattern) before being acknowledged, so a crash between "trip completed" and "trip persisted" cannot silently drop history data — this is the concrete mechanism that makes the RPO achievable. |
| Planned maintenance window | **Excluded from SLA calculation, ≤ 1 hour/month, announced** | Standard practice for a small deployment — separates "we chose to restart for a deploy" from "it broke." |

---

## 2. Performance

**What the BRD says:** Nothing numeric. The only performance-adjacent
statement is qualitative — dispatch should be "smart" and "efficient" to
conserve fuel ([brd-analysis.md §4.2](brd-analysis.md)) — which is a *decision
quality* goal, not a *latency* goal, and the two are handled separately below.

| Requirement | Target | Reasoning |
|---|---|---|
| API response time — call/dispatch endpoint (p50) | **< 100ms** | Standard "feels instant" threshold (Nielsen's 100ms UI-response heuristic) applied to the write path; achievable against an embedded/file-based DB with no network hop to a separate DB server. |
| API response time — call/dispatch endpoint (p95) | **< 300ms** | Covers the atomic capacity-check-and-reserve operation required by Edge Case #4 ([brd-analysis.md §6, row 4](brd-analysis.md)), which needs a lock/transaction and will occasionally be slower than the median under contention. |
| API response time — call/dispatch endpoint (p99) | **< 750ms** | Tail budget for worst-case lock contention when multiple docks call simultaneously (same Edge Case #4) without letting a rare slow request read as "broken." |
| API response time — history/stats read endpoint (p95) | **< 500ms** | This endpoint does aggregation (trip counts per destination, per [brd-analysis.md §4.3](brd-analysis.md)) rather than a single-row lookup, so it gets a looser budget than the write path — still well within "instant" territory for an internal/analyst-facing read. |
| Dispatch decision latency (the "smart" assignment algorithm itself) | **< 200ms**, included inside the p95 above | The dispatch algorithm ([brd-analysis.md §4.2](brd-analysis.md)) runs synchronously as part of the call flow — evaluating 4 shuttles' state is cheap (fixed, small fleet size), so this should be a small fraction of the overall request budget, not a separate slow step. |
| Frontend page load — First Contentful Paint | **< 1.5s** on a typical broadband connection | Standard web-vitals "good" threshold; the UI is a small SPA (5 dock views + one call form), so there's no justification for anything slower. |
| Frontend page load — Time to Interactive | **< 2.5s** | Matches Core Web Vitals "good" bucket; keeps the call-a-shuttle form usable quickly, which matters most since it's the one action the whole BRD is built around ([brd-analysis.md §4.1](brd-analysis.md)). |

---

## 3. Scalability

**What the BRD says:** The fleet is fixed at 4 shuttles and the planet set at
5 ([brd-analysis.md §5, rules 1–2](brd-analysis.md)), which bounds the
*physical* domain but not the *request volume* — many calls can still arrive
over time against that fixed fleet. The BRD's own "scalable, future-proofed"
goal is explicitly in tension with the hardcoded counts, a tension already
flagged and resolved by assumption (fleet/planet counts configurable,
[brd-analysis.md §7.9](brd-analysis.md)) rather than by any number.

| Requirement | Target | Reasoning |
|---|---|---|
| Expected concurrent users | **≥ 50 concurrent simulated passengers/callers**, single instance, no degradation past the p95 targets in §2 | Confirmed demo-scale target. 50 is a deliberately generous ceiling for a 5-dock, 4-shuttle system being evaluated by one reviewer — high enough to prove the atomic capacity-check under load (Edge Case #4), not sized for real multi-tenant traffic. |
| Concurrent-call correctness under peak load | **Zero overbooking incidents** across a load test of ≥ 20 simultaneous calls converging on the same near-capacity shuttle | This is the measurable proof of Edge Case #4 and Business Rule 3's dual-cap check ([brd-analysis.md §5, rule 3](brd-analysis.md) / [§6, row 4](brd-analysis.md)) — correctness under concurrency matters more than raw throughput for this domain. |
| Data growth — travel history | Sustain **≥ 100,000 history records** (successful + rejected/failed, per [brd-analysis.md §7.12](brd-analysis.md)) with the stats-read p95 in §2 still holding | Every attempted call is logged, not just completed trips, so history grows faster than trip count alone. 100k is a reasonable multi-year volume for a 4-shuttle fleet and is a fair stress test for an embedded/file-based DB (e.g. SQLite) before it would need replacing. |
| Peak load scenario | **All 5 docks calling simultaneously**, repeatedly, sustained for several minutes, with dispatch latency staying inside the p95/p99 bounds in §2 | Directly modeled from the domain shape (5 fixed docks) rather than an arbitrary number — this is the actual worst case the system can ever see given a fixed planet count. |
| Fleet/planet growth path | Increasing fleet size or planet count is a **config change only** — no code change or redeploy of dispatch logic required | Directly implements the configurability assumption already adopted in the BRD analysis ([brd-analysis.md §7.9](brd-analysis.md)); this is the concrete NFR that makes "future-proofed" testable rather than aspirational. |

---

## 4. Security

**What the BRD says:** Nothing at all — no mention of accounts, login, roles,
or data protection. The BRD analysis already concluded, as a working
assumption, that authentication is out of scope and passengers are anonymous
per-call ([brd-analysis.md §7.1–§7.2](brd-analysis.md)). That assumption is
adopted here as given (confirmed in this session's scoping Q&A), so most of
this section documents *why a target is N/A* rather than proposing a number.

| Requirement | Target | Reasoning |
|---|---|---|
| Authentication method | **None required for this challenge** — explicitly out of scope | Matches the BRD analysis's own conclusion; no login is described anywhere in the source BRD, and passengers are identified only by species + weight, not identity ([brd-analysis.md §2](brd-analysis.md)). If this became a real production system, the honest answer would be OIDC/OAuth2 with a passenger identity — noted here so the gap is visible, not silently dropped. |
| Authorization model | **Single implicit role, unrestricted access** — no permission checks needed | There is exactly one behavioral actor described (Passenger); the only other consumer implied — the "team" studying travel stats ([brd-analysis.md §2](brd-analysis.md)) — has no distinct permissions defined either, so there's nothing to enforce differently between them for this challenge. |
| Data encryption — in transit | **100% of API traffic over HTTPS/TLS 1.2+** | Baseline hygiene that costs nothing to add even without PII — protects the integrity of dispatch/capacity decisions in transit (an attacker tampering with a call's weight/count could otherwise cause silent overbooking). This is the one security control worth stating as a hard target regardless of the "no compliance" scoping. |
| Data encryption — at rest | **Not required; optional hardening only** | No PII is collected (species is a free-text label, weight is a number — [brd-analysis.md §7.4](brd-analysis.md)), so there's no sensitive-data classification driving an at-rest encryption requirement. Encrypting the embedded DB file is a reasonable future hardening step, not a target to hold this build to. |
| Compliance needs | **None applicable** (confirmed) | No PII, no payments, no health data — none of GDPR/HIPAA/PCI-DSS etc. are triggered by this domain as scoped. This is a scope statement, not a gap. |
| Input validation (adjacent to security) | **Reject invalid input at the API boundary**: unknown planet names, non-positive weight/count, origin == destination | Not "security" in the confidentiality sense, but worth stating here as the concrete defensive-coding target implementing Edge Cases #2, #5, #9 and Business Rule 4 ([brd-analysis.md §5–§6](brd-analysis.md)) — malformed input should fail fast with a validation error, never reach the dispatch/capacity logic. |

---

## 5. Cost

**What the BRD says:** One explicit constraint — the backend "must run
without a complex database... simple to run" ([brd-analysis.md §5, rule
6](brd-analysis.md)) — which is a cost/ops-simplicity requirement in disguise,
even though the BRD never frames it in dollars. No budget figure is given.

| Requirement | Target | Reasoning |
|---|---|---|
| Infrastructure budget | **$0 / no paid dependencies required to run or evaluate the solution** | This is a take-home challenge, not a funded project — a reviewer needs to run it locally or on a free tier with zero setup cost. Anything requiring a paid managed service would contradict the BRD's own "simple to run" instruction. |
| Hosting model | **Self-hosted, single Docker container**, embedded/file-based DB (e.g. SQLite/LiteDB) bundled in the same container | Directly implements Business Rule 6's "no complex DB setup" ([brd-analysis.md §5](brd-analysis.md)) as literally as possible — one `docker run`/`docker-compose up` with no external DB server, no cloud account, no provisioning step. |
| Cloud provider preference | **None — explicitly avoided for this challenge** | A managed cloud deployment (AWS/Azure/GCP) adds account setup, IAM, and provisioning friction that works against "simple to run." If this were a real production launch, this is the line item that would flip first — noted here as the known future trade-off, not solved now. |
| Ongoing operational cost | **$0/month** — runs on a developer machine or a single free-tier VM/container host | Consistent with the demo-scale, self-hosted decisions above; there is no traffic volume in this scope that would require paid compute. |

---

## Summary

None of the five NFR categories had a stated numeric target in the source
BRD — every number above is a proposed default, not an extracted fact, and
each is traceable back either to a specific BRD statement/business
rule/edge case (availability's RPO, performance's concurrency budget,
scalability's peak-load scenario, security's input validation, cost's
hosting model) or to the four scoping decisions confirmed at the top of this
document (challenge context, self-hosted single container, demo scale, no
compliance regime). If any of those four scoping decisions changes — most
likely "challenge submission" flipping to "real production deployment" —
every target in this document should be revisited together, since they were
derived as a consistent set, not independently.
