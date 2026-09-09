# How to run

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` should report `10.x`). Nothing else — no
  database server, no container, no build tooling.

## Run

```bash
dotnet run --project src/backend/SpaceTravel.Api
```

On first start the app creates `src/backend/SpaceTravel.Api/spacetravel.db`, applies its
migration, and seeds the world. It then listens on the URL printed in the console
(typically `http://localhost:5000`). Restarting keeps everything — the seeder is
idempotent and never duplicates the fleet.

> The `.db` file usually looks tiny (4 KB) because EF Core enables SQLite WAL mode;
> the live data sits in the neighbouring `spacetravel.db-wal` until checkpoint. This
> is normal — the database is not empty.

To start over, delete `spacetravel.db*` and run again.

## Test

```bash
dotnet test
```

52 tests. The domain suite runs with no database at all:

```bash
dotnet test --filter "FullyQualifiedName~SpaceTravel.Tests.Domain"
```

## The seeded world

| Planet id | Planet | Distance rank |
|---|---|---|
| 1 | Angel 1 | 1 (closest) |
| 2 | Boreth | 2 |
| 3 | Aurelia | 3 |
| 4 | Blue Horizon | 4 |
| 5 | Argus X | 5 (farthest) |

Four shuttles (`Shuttle 1`–`Shuttle 4`), all idle at Angel 1, each carrying up to
**20 life forms or 4000 kg**.

A leg takes `max(1, |rank difference|) × 3 seconds`, so Angel 1 → Argus X is 12
seconds. All of this is configurable in
[`appsettings.json`](../src/backend/SpaceTravel.Api/appsettings.json) — planets, fleet size,
capacity caps and simulation timings are configuration, not code.

## API

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/travel-requests` | Call a shuttle |
| GET | `/api/travel-requests/{id}` | Check one call |
| GET | `/api/shuttles` | Live fleet state |
| GET | `/api/planets` | The 5 space docks |
| GET | `/api/travel-history?page=&pageSize=` | Paged outcome log |
| GET | `/api/travel-history/stats` | Trips per destination |
| GET | `/health` | Liveness |

## Walkthrough

Call a shuttle from Angel 1 to Argus X:

```bash
curl -s -X POST http://localhost:5000/api/travel-requests \
  -H "Content-Type: application/json" \
  -d '{
        "originPlanetId": 1,
        "destinationPlanetId": 5,
        "lifeForms": [
          { "species": "Vulcan", "weightKg": 68 },
          { "species": "Gorn",   "weightKg": 210.5 }
        ]
      }'
```

```json
{
  "travelRequestId": 1,
  "status": "Assigned",
  "outcome": "Assigned",
  "lifeFormCount": 2,
  "totalWeightKg": 278.5,
  "shuttleId": 1,
  "shuttleName": "Shuttle 1"
}
```

Watch the fleet — within half a second Shuttle 1 is en route with an ETA:

```bash
curl -s http://localhost:5000/api/shuttles
```

Poll the call until it lands:

```bash
curl -s http://localhost:5000/api/travel-requests/1
```

After ~12 seconds it reads `"status": "Completed"`, still naming the shuttle that
flew it. Then ask the question the brief actually cares about:

```bash
curl -s http://localhost:5000/api/travel-history/stats
```

```json
[ { "planet": "Argus X", "distanceRank": 5, "completedTrips": 1,
    "rejectedCalls": 0, "lifeFormsDelivered": 2, "weightDeliveredKg": 278.5 }, ... ]
```

### Things worth trying

```bash
# Batching: two calls going the same way share one shuttle — one launch, not two.
# Send these back to back and compare shuttleId.
curl -s -X POST http://localhost:5000/api/travel-requests -H "Content-Type: application/json" \
  -d '{"originPlanetId":1,"destinationPlanetId":5,"lifeForms":[{"species":"Vulcan","weightKg":68}]}'

# Rejected: 21 life forms exceeds the 20 cap. Returns 201 with outcome "Rejected"
# and is recorded in history — a dock turning parties away is a finding, not an error.
# Rejected: one 5000kg life form exceeds the 4000kg cap.
curl -s -X POST http://localhost:5000/api/travel-requests -H "Content-Type: application/json" \
  -d '{"originPlanetId":1,"destinationPlanetId":4,"lifeForms":[{"species":"Horta","weightKg":5000}]}'

# Invalid: you cannot call a shuttle to the planet you are standing on. Returns 400.
curl -s -X POST http://localhost:5000/api/travel-requests -H "Content-Type: application/json" \
  -d '{"originPlanetId":1,"destinationPlanetId":1,"lifeForms":[{"species":"Vulcan","weightKg":68}]}'
```

A scripted version of the whole walkthrough, with expected results, is in
[`scripts/e2e.ps1`](../scripts/e2e.ps1) — start the app on port 5090 and run it.

## Why 201 for a rejected call?

A `400` means "your request was malformed". A rejection means "your request was
perfectly well formed, and the answer is no shuttle can carry this" — a real
business outcome, recorded and retrievable by id. Conflating the two would hide
exactly the demand signal the brief asks the system to capture. Malformed input
still gets a `400` with RFC 7807 ProblemDetails.
