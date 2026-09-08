# Backend Implementation Specification — Space Travel System

Scope: ASP.NET Core Web API backend for the locked architecture (Modular Monolith, EF Core + SQLite, SignalR for live updates). Builds directly on the locked v1 assumptions and architecture decision already agreed.

---

## 1. Entity Definitions

### 1.1 Enums

```csharp
public enum ShuttleStatus
{
    Idle = 0,
    EnRouteToPickup = 1,
    Boarding = 2,
    InTransit = 3
}

public enum TravelRequestStatus
{
    Pending = 0,    // queued, no shuttle assigned yet
    Assigned = 1,   // shuttle en route to pickup
    Boarding = 2,
    InTransit = 3,
    Completed = 4,
    Rejected = 5    // failed validation that can never be served (see 3.1 error scenarios)
}

public enum Species
{
    Human = 0,
    Zorgon = 1,
    Xenomorph = 2,
    Custom = 3      // requires an explicit WeightKg on the request
}
```

### 1.2 Value Objects

```csharp
// Immutable, structurally-equal — one life form on a manifest. Not independently tracked
// across requests (per locked assumption: passengers are anonymous/ephemeral).
public sealed record LifeForm(Species Species, decimal WeightKg);

public static class SpeciesDefaults
{
    public static readonly IReadOnlyDictionary<Species, decimal> DefaultWeightKg = new Dictionary<Species, decimal>
    {
        [Species.Human] = 75m,
        [Species.Zorgon] = 120m,
        [Species.Xenomorph] = 95m
        // Species.Custom has no default — WeightKg must be supplied by the caller
    };
}
```

### 1.3 Entities

```csharp
public class Planet
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public int DistanceUnit { get; set; } // 1 = closest (Angel 1) .. 5 = farthest (Argus X)

    public ICollection<TravelRequest> DepartingRequests { get; set; } = new List<TravelRequest>();
    public ICollection<TravelRequest> ArrivingRequests { get; set; } = new List<TravelRequest>();
}

public class Shuttle
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public ShuttleStatus Status { get; set; } = ShuttleStatus.Idle;
    public int? CurrentPlanetId { get; set; }
    public Planet? CurrentPlanet { get; set; }

    public ICollection<TravelRequest> TravelRequests { get; set; } = new List<TravelRequest>();

    public const int MaxLifeForms = 20;
    public const decimal MaxWeightKg = 4000m;
}

public class TravelRequest
{
    public int Id { get; set; }

    public int OriginPlanetId { get; set; }
    public Planet OriginPlanet { get; set; } = default!;
    public int DestinationPlanetId { get; set; }
    public Planet DestinationPlanet { get; set; } = default!;

    public List<LifeForm> LifeForms { get; set; } = new();
    public int TotalHeadcount { get; set; }
    public decimal TotalWeightKg { get; set; }

    public TravelRequestStatus Status { get; set; } = TravelRequestStatus.Pending;
    public int? ShuttleId { get; set; }
    public Shuttle? Shuttle { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime RequestedAtUtc { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
```

**Note on scope:** `TravelRequest` unifies what earlier BA analysis called "Travel Request," "Trip," and "Travel History" into one entity, since a locked v1 assumption is one request = one trip = one shuttle (no batching, no multi-stop). Stats/history queries simply filter this table by `Status == Completed`. `Dock` was not modeled as its own entity — it's 1:1 with `Planet` with no independent behavior, so it's folded into `Planet` (would be split out if that assumption changes). Shuttle does **not** carry a redundant "current active request" foreign key — the active request (if any) is derived by querying `TravelRequests` for that `ShuttleId` with `Status` in `{Assigned, Boarding, InTransit}`, enforced as at-most-one via a filtered unique index (see §2.2).

### 1.4 Relationships

| From | To | Cardinality | Notes |
|---|---|---|---|
| Planet | TravelRequest (as Origin) | 1 — * | `OriginPlanetId` FK |
| Planet | TravelRequest (as Destination) | 1 — * | `DestinationPlanetId` FK |
| Shuttle | TravelRequest | 1 — * (0..1 active at a time) | `ShuttleId` FK, nullable while `Pending` |
| Planet | Shuttle (current location) | 1 — * | `CurrentPlanetId` FK, nullable |
| TravelRequest | LifeForm | 1 — * (owned/embedded, not a table) | stored as JSON, see §2.2 |

---

## 2. Database

### 2.1 DbContext

```csharp
public class SpaceTravelDbContext : DbContext
{
    public SpaceTravelDbContext(DbContextOptions<SpaceTravelDbContext> options) : base(options) { }

    public DbSet<Planet> Planets => Set<Planet>();
    public DbSet<Shuttle> Shuttles => Set<Shuttle>();
    public DbSet<TravelRequest> TravelRequests => Set<TravelRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SpaceTravelDbContext).Assembly);
    }
}
```

### 2.2 EF Core Mapping Configuration

```csharp
public class PlanetConfiguration : IEntityTypeConfiguration<Planet>
{
    public void Configure(EntityTypeBuilder<Planet> builder)
    {
        builder.ToTable("Planets");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(50);
        builder.HasIndex(p => p.Name).IsUnique();
        builder.Property(p => p.DistanceUnit).IsRequired();

        builder.HasData(
            new Planet { Id = 1, Name = "Angel 1",      DistanceUnit = 1 },
            new Planet { Id = 2, Name = "Boreth",       DistanceUnit = 2 },
            new Planet { Id = 3, Name = "Aurelia",      DistanceUnit = 3 },
            new Planet { Id = 4, Name = "Blue Horizon", DistanceUnit = 4 },
            new Planet { Id = 5, Name = "Argus X",      DistanceUnit = 5 }
        );
    }
}

public class ShuttleConfiguration : IEntityTypeConfiguration<Shuttle>
{
    public void Configure(EntityTypeBuilder<Shuttle> builder)
    {
        builder.ToTable("Shuttles");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(s => s.CurrentPlanet)
            .WithMany()
            .HasForeignKey(s => s.CurrentPlanetId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasData(
            new Shuttle { Id = 1, Name = "Shuttle-01", Status = ShuttleStatus.Idle },
            new Shuttle { Id = 2, Name = "Shuttle-02", Status = ShuttleStatus.Idle },
            new Shuttle { Id = 3, Name = "Shuttle-03", Status = ShuttleStatus.Idle },
            new Shuttle { Id = 4, Name = "Shuttle-04", Status = ShuttleStatus.Idle }
        );
    }
}

public class TravelRequestConfiguration : IEntityTypeConfiguration<TravelRequest>
{
    public void Configure(EntityTypeBuilder<TravelRequest> builder)
    {
        builder.ToTable("TravelRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TotalWeightKg).HasColumnType("decimal(10,2)");

        // Two FKs to the same table (Planet) — set both to Restrict so SQLite/EF don't
        // have to reason about ambiguous cascade paths on Planet deletion (planets are
        // seed/reference data and are never deleted at runtime anyway).
        builder.HasOne(r => r.OriginPlanet)
            .WithMany(p => p.DepartingRequests)
            .HasForeignKey(r => r.OriginPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DestinationPlanet)
            .WithMany(p => p.ArrivingRequests)
            .HasForeignKey(r => r.DestinationPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Shuttle)
            .WithMany(s => s.TravelRequests)
            .HasForeignKey(r => r.ShuttleId)
            .OnDelete(DeleteBehavior.SetNull);

        // LifeForms persisted as a JSON string column via a value converter — SQLite has no
        // native JSON column type in this EF Core version, and a normalized child table would
        // be more ceremony than a handful of embedded values need (matches "keep DB simple").
        var lifeFormsComparer = new ValueComparer<List<LifeForm>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, lf) => HashCode.Combine(hash, lf.Species, lf.WeightKg)),
            v => v.ToList());

        builder.Property(r => r.LifeForms)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<LifeForm>>(v, (JsonSerializerOptions?)null) ?? new())
            .HasColumnName("LifeFormsJson")
            .HasColumnType("TEXT")
            .Metadata.SetValueComparer(lifeFormsComparer);

        // Enforces "at most one active request per shuttle" at the DB level, not just in app code.
        builder.HasIndex(r => r.ShuttleId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Assigned','Boarding','InTransit')");

        builder.HasIndex(r => r.Status); // fast lookup of the Pending queue and Completed history
    }
}
```

### 2.3 Migrations

```
dotnet ef migrations add InitialCreate -p SpaceTravel.Infrastructure -s SpaceTravel.Api -o Persistence/Migrations
dotnet ef database update -p SpaceTravel.Infrastructure -s SpaceTravel.Api
```

- Connection string (`appsettings.json`): `"ConnectionStrings": { "Default": "Data Source=spacetravel.db" }` — a single file in the working directory, no server install.
- `IDesignTimeDbContextFactory<SpaceTravelDbContext>` implemented in `Infrastructure` so `dotnet ef` works from the CLI without spinning up the full DI host.
- **Auto-migrate on startup** (`Program.cs`, before `app.Run()`):
  ```csharp
  using (var scope = app.Services.CreateScope())
  {
      var db = scope.ServiceProvider.GetRequiredService<SpaceTravelDbContext>();
      await db.Database.MigrateAsync();
      if (app.Environment.IsDevelopment())
          await DemoDataSeeder.SeedAsync(db);
  }
  ```
  This means `dotnet run` alone creates and migrates the DB file — no manual `dotnet ef` step required to get the app running, matching the "easy to run" requirement.

### 2.4 Seeding Strategy

Two distinct kinds of seed data, seeded two different ways:

1. **Fixed reference data** — 5 Planets, 4 Shuttles. Deterministic, real domain values, applied automatically via EF Core migrations' `HasData` (shown in §2.2). Not randomized.
2. **Historical demo data** — a batch of plausible `Completed` `TravelRequest`s so the stats endpoint and fleet-view aren't empty on first run. Generated at startup (Development only) with **Bogus**, only if the table is empty:

```csharp
public static class DemoDataSeeder
{
    private static readonly Dictionary<Species, (decimal min, decimal max)> SpeciesWeightRanges = new()
    {
        [Species.Human] = (55m, 100m),
        [Species.Zorgon] = (90m, 160m),
        [Species.Xenomorph] = (70m, 130m)
    };

    public static async Task SeedAsync(SpaceTravelDbContext db)
    {
        if (await db.TravelRequests.AnyAsync()) return;

        var planetIds = await db.Planets.Select(p => p.Id).ToListAsync();
        var shuttleIds = await db.Shuttles.Select(s => s.Id).ToListAsync();

        var lifeFormFaker = new Faker<LifeForm>()
            .CustomInstantiator(f =>
            {
                var species = f.PickRandom(Species.Human, Species.Zorgon, Species.Xenomorph);
                var (min, max) = SpeciesWeightRanges[species];
                return new LifeForm(species, Math.Round(f.Random.Decimal(min, max), 1));
            });

        var requestFaker = new Faker<TravelRequest>()
            .CustomInstantiator(f =>
            {
                var origin = f.PickRandom(planetIds);
                var destination = f.PickRandom(planetIds.Where(id => id != origin).ToList());
                var lifeForms = lifeFormFaker.Generate(f.Random.Int(1, 6));

                // Clamp so seeded data can never itself violate the capacity business rule.
                while (lifeForms.Count > Shuttle.MaxLifeForms || lifeForms.Sum(l => l.WeightKg) > Shuttle.MaxWeightKg)
                    lifeForms.RemoveAt(lifeForms.Count - 1);

                var requestedAt = f.Date.Recent(30);
                return new TravelRequest
                {
                    OriginPlanetId = origin,
                    DestinationPlanetId = destination,
                    LifeForms = lifeForms,
                    TotalHeadcount = lifeForms.Count,
                    TotalWeightKg = lifeForms.Sum(l => l.WeightKg),
                    Status = TravelRequestStatus.Completed,
                    ShuttleId = f.PickRandom(shuttleIds),
                    RequestedAtUtc = requestedAt,
                    AssignedAtUtc = requestedAt.AddMinutes(1),
                    CompletedAtUtc = requestedAt.AddMinutes(f.Random.Int(15, 90))
                };
            });

        db.TravelRequests.AddRange(requestFaker.Generate(80));
        await db.SaveChangesAsync();
    }
}
```

---

## 3. API Endpoints

Shared error shape (extends the standard ASP.NET Core `ProblemDetails` with a machine-readable code):

```csharp
public sealed record ApiErrorResponse(string Code, string Message);
```

### 3.1 `POST /api/travel-requests`

**Request**
```csharp
public sealed record LifeFormDto(Species Species, decimal? WeightKg);

public sealed record CreateTravelRequestRequest(
    int OriginPlanetId,
    int DestinationPlanetId,
    List<LifeFormDto> LifeForms);
```

**Response — `201 Created`** (`Location: /api/travel-requests/{id}`)
```csharp
public sealed record TravelRequestResponse(
    int Id,
    int OriginPlanetId,
    int DestinationPlanetId,
    TravelRequestStatus Status,
    int? ShuttleId,
    int TotalHeadcount,
    decimal TotalWeightKg,
    DateTime RequestedAtUtc,
    DateTime? AssignedAtUtc,
    DateTime? CompletedAtUtc);
```

**Business rules & validation**
1. `OriginPlanetId` and `DestinationPlanetId` must reference existing planets and must differ.
2. `LifeForms` must contain at least one entry.
3. For `Species.Custom`, `WeightKg` is required; for known species, `WeightKg` is optional and defaults from `SpeciesDefaults`.
4. Any single resolved life form weight `> 4000kg` can never be served by any shuttle — rejected outright.
5. Manifest totals (`headcount > 20` **or** `totalWeight > 4000kg`) can never fit in one shuttle (no splitting a single request across shuttles, no batching in v1) — rejected outright.
6. If validation passes, the request is persisted as `Pending`; the dispatch service then attempts immediate assignment to the nearest idle shuttle (by `DistanceUnit` proximity to the origin planet). If assigned: `Status → Assigned`, `ShuttleId` set, `AssignedAtUtc` set, SignalR broadcast fired. If no shuttle is idle: it stays `Pending` in the in-memory FIFO queue and is re-evaluated whenever a shuttle becomes idle.

**Error scenarios**

| Status | Code | Trigger |
|---|---|---|
| 400 | `PLANET_NOT_FOUND` | origin or destination id doesn't exist |
| 400 | `ORIGIN_EQUALS_DESTINATION` | origin and destination are the same planet |
| 400 | `EMPTY_MANIFEST` | no life forms supplied |
| 400 | `WEIGHT_REQUIRED_FOR_CUSTOM_SPECIES` | `Species.Custom` without `WeightKg` |
| 422 | `SINGLE_LIFEFORM_EXCEEDS_CAPACITY` | one life form's weight alone > 4000kg |
| 422 | `MANIFEST_EXCEEDS_SHUTTLE_CAPACITY` | total headcount > 20 or total weight > 4000kg |

### 3.2 `GET /api/planets`

**Response — `200 OK`**
```csharp
public sealed record PlanetResponse(int Id, string Name, int DistanceUnit);
// -> List<PlanetResponse>
```
Static reference data; no business rules, no error scenarios beyond generic 500.

### 3.3 `GET /api/shuttles`

**Response — `200 OK`**
```csharp
public sealed record ShuttleResponse(
    int Id,
    string Name,
    ShuttleStatus Status,
    int? CurrentPlanetId,
    int? ActiveTravelRequestId);
// -> List<ShuttleResponse>
```
`ActiveTravelRequestId` is computed via a join/query (see §1.3 note), not a stored column. Used for the initial fleet-view snapshot; the same shape is pushed incrementally over SignalR (§3.6).

### 3.4 `GET /api/travel-requests/{id}`

**Response — `200 OK`**: `TravelRequestResponse` (same shape as §3.1).
**Error:** `404 TRAVEL_REQUEST_NOT_FOUND` if the id doesn't exist.
Purpose: initial fetch for a specific request, and a fallback if a SignalR connection drops.

### 3.5 `GET /api/history/stats`

**Response — `200 OK`**
```csharp
public sealed record PlanetTravelStatsResponse(int PlanetId, string PlanetName, int CompletedTripCount);
// -> List<PlanetTravelStatsResponse>, ordered by CompletedTripCount descending
```
**Business rule:** counts `TravelRequest`s where `Status == Completed`, grouped by `DestinationPlanetId`; planets with zero completed trips are still included with `CompletedTripCount = 0` (left join from `Planets`). No error scenarios beyond generic 500.

### 3.6 SignalR Hub — `/hubs/fleet`

Not a REST endpoint — a broadcast channel for live UI updates, consumed by the React frontend via `@microsoft/signalr`.

- **Server → client methods:**
  - `ShuttleStatusChanged(ShuttleResponse shuttle)`
  - `TravelRequestStatusChanged(TravelRequestResponse request)`
- **No client → server methods in v1** — all mutations go through `POST /api/travel-requests`; the hub is broadcast-only.
- SignalR does **not** replay history to a newly connected client — the frontend must call `GET /api/shuttles` (and `GET /api/travel-requests/{id}` where relevant) for the initial snapshot, then apply hub pushes as deltas.

---

## Validation implementation note

Structural request validation (required fields, valid enum values, non-negative weights) belongs in a `FluentValidation` validator for `CreateTravelRequestRequest`, run as an ASP.NET Core filter/pipeline behavior. The capacity business rules (§3.1 rules 4–5) are **not** pure input validation — they depend on the domain constants `Shuttle.MaxLifeForms`/`MaxWeightKg` — so they belong in the Application-layer use case (`CreateTravelRequestHandler`), not the validator, keeping FluentValidation focused on shape and the Application layer focused on business rules.
