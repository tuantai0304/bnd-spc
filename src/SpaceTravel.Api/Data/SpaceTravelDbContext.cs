using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data;

/// <summary>
/// Maps the domain model straight to SQLite — there are no separate persistence
/// classes to keep in sync. The entity configurations do the work of reaching
/// private setters and backing fields so the domain keeps its invariants.
/// </summary>
public sealed class SpaceTravelDbContext : DbContext
{
    public SpaceTravelDbContext(DbContextOptions<SpaceTravelDbContext> options)
        : base(options)
    {
    }

    public DbSet<Planet> Planets => Set<Planet>();
    public DbSet<Shuttle> Shuttles => Set<Shuttle>();
    public DbSet<TravelRequest> TravelRequests => Set<TravelRequest>();
    public DbSet<TravelHistoryEntry> TravelHistory => Set<TravelHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SpaceTravelDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
