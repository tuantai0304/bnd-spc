# Vertical Slice Architecture Assessment

Add "Option D — Vertical Slice Architecture" to
[`docs/architecture-options.md`](../docs/architecture-options.md), scored
against the same six dimensions and NFR categories as Options A/B/C, update
the comparison table and recommendation to account for it, and be explicit
about the one architectural nuance that matters here: VSA is a **code
organization style for a single deployable**, not an infrastructure choice
like A/B/C — so its deployment model, DB choice, and auth strategy are
identical to Option A's, and it is combinable with (not necessarily
exclusive from) Option A's module boundaries. The write-up must say this
plainly rather than inflate VSA into a false peer of "microservices" or
"serverless" on infrastructure grounds.

**Confirmed scope decisions (do not re-litigate):**
- VSA is added as a 4th top-level option (Option D) in the existing document,
  not folded silently into Option A and not a standalone document.
- Output location: append to `docs/architecture-options.md` (§2 renumbers to
  make room, or VSA is inserted as new §2 with B/C renumbering to §3/§4 —
  whichever keeps the document's existing internal anchor links consistent;
  decide during drafting and verify no broken in-document links remain).
- Stack: .NET / C#, consistent with the rest of the document (e.g. Minimal
  APIs + MediatR-per-slice, or FastEndpoints as the concrete VSA-flavored
  library choice).

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when the phase is
done, set its status to `Complete` and write its **Phase Summary**; run the
**Verification Plan** and record the result. When complete, fill in
**Final Recap**. **Deployment Plan** is N/A (documentation-only change).

## Phase 1: Draft and integrate the VSA option
Status: Complete

- [x] Write "Option D — Vertical Slice Architecture" covering all six required
      dimensions: architecture style (per-use-case slices — e.g.
      `CallShuttle/`, `GetHistoryStats/` — each with its own
      request/handler/validator/response, no shared service/repository
      layers); project structure (concrete `Features/<UseCase>/` folder
      layout replacing the Fleet/Dispatch/Planets/History module split from
      Option A); DB choice (same as Option A — SQLite/EF Core — call out
      explicitly that this dimension is unchanged); API design (Minimal API
      endpoint colocated per slice, or FastEndpoints, still REST +
      SignalR for live shuttle state as in Option A); auth strategy (same
      as Option A — none, per confirmed scope); deployment model (same
      single-container Docker deployment as Option A — call out explicitly
      that this is unchanged).
- [x] Explicitly address where the atomic capacity check-and-reserve
      (Edge Case #4) lives under VSA — does each slice call into the same
      in-memory Fleet singleton from Option A, or does VSA imply duplicating
      that logic per slice (a real risk to flag, since "bug-free" is a
      top-level BRD goal and slice-per-feature duplication is VSA's most
      commonly cited failure mode).
- [x] Write pros/cons specific to VSA versus Option A's layered/modular
      split: cross-cutting-logic duplication risk, per-feature testability
      and onboarding speed, coupling of request shape to persistence shape,
      how it affects the "module boundary keeps a future extraction path
      open" argument Option A relies on for the "future-proofed" NFR goal.
- [x] Map pros/cons to the NFR categories in `nfr-analysis.md` the same way
      Options A/B/C were mapped — note explicitly which categories are
      unaffected (Cost, Availability, Security, most of Performance/Scalability)
      because the infrastructure is identical to Option A, and which are
      actually affected (maintainability/bug-free-goal-adjacent concerns from
      duplication risk, and developer velocity, which isn't a named NFR
      category but is worth a sentence tying back to the BRD's "bug-free"
      top-level goal).
- [x] State team size/expertise (similar to Option A: 1-2 engineers, plus
      familiarity with the vertical-slice/CQRS-lite pattern specifically) and
      complexity estimate (Low, same infra as Option A, code-organization
      learning curve only).
- [x] Update §4 comparison table to add a Vertical Slice Architecture column.
- [x] Update §5 recommendation: state plainly that VSA is not a competing
      infrastructure choice against Option A but a code-organization decision
      *within* it, and give a concrete recommendation on whether the
      Modular Monolith should be implemented using vertical slices,
      traditional layers, or a hybrid (slices within each of the existing
      Fleet/Dispatch/Planets/History modules) — pick one and justify it
      rather than leaving it open.
- [x] Fix any renumbered section headers/anchors so all in-document links
      (e.g. "see §4", "[nfr-analysis.md §2]") still resolve correctly.

### Verification Plan
- `docs/architecture-options.md` contains an "Option D" (or equivalently
  titled) section covering all six required dimensions, pros/cons, NFR
  mapping, team size, and complexity, matching the depth of Options A/B/C.
- The comparison table in the document includes a fourth column for VSA.
- The recommendation section explicitly resolves whether/how VSA applies to
  the already-recommended Modular Monolith — no open question left dangling.
- Grep the file for section-reference strings (e.g. "§4", "§5") and confirm
  each still points at the section it names after any renumbering.

### Phase Summary
Inserted a new "§4. Option D — Vertical Slice Architecture" into
`docs/architecture-options.md` (renumbering the old §4 Comparison → §5 and
§5 Recommendation → §6, and fixing the one stale in-document section
reference in the intro). Option D was framed honestly up front as a
code-organization style for a single deployable, not an infrastructure peer
of B/C — its DB/auth/deployment dimensions are stated as identical to
Option A's, with architecture style, project structure, and duplication-risk
trade-offs as the only real deltas. Explicitly addressed where atomic
dispatch state lives under VSA (a shared, non-duplicated `FleetState` /
`ShuttleStateMachine` in a `Shared/` folder — flagged duplication of that
invariant across slices as the concrete "bug-free"-goal risk). Added a
4-column comparison table and a new §6.1 that resolves the actual open
question plainly: recommended a **hybrid** — keep Option A's four
bounded-context modules (Fleet/Dispatch/Planets/History) as the top-level
structure, and organize each module's use cases internally as vertical
slices. Verified no other in-document §-references broke from the
renumbering (grepped all `§\d` occurrences; all resolve correctly after the
one intro-line fix).

## Final Recap
Assessed Vertical Slice Architecture as a 4th option in
`docs/architecture-options.md`, correctly identifying it as a code-org
pattern layered onto Option A rather than a true infrastructure alternative
like B or C. Delivered a concrete, non-hedged recommendation: use Option A's
bounded-context module boundaries at the top level, and vertical slices for
each module's individual use cases, with the atomic dispatch/capacity
invariant centralized once (not duplicated per slice) to protect the BRD's
"bug-free" goal. No code was written; this remains a documentation-only
architecture decision.

## Deployment Plan
N/A — documentation-only change, no deployable artifact.
