using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceTravel.Api.Code.Options;
using SpaceTravel.Api.Data;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Tests.Data;

/// <summary>
/// Proves the migration applies to a real SQLite file and that the world it
/// seeds matches the brief: 5 planets in distance order, 4 shuttles at 20/4000.
/// </summary>
public sealed class DbSeederTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"spacetravel-test-{Guid.NewGuid():N}.db");

    private SpaceTravelDbContext NewContext()
        => new(new DbContextOptionsBuilder<SpaceTravelDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options);

    private static DbSeeder SeederFor(SpaceTravelDbContext db) => new(
        db,
        Options.Create(new PlanetOptions
        {
            Known =
            [
                new PlanetSeed { Name = "Angel 1", DistanceRank = 1 },
                new PlanetSeed { Name = "Boreth", DistanceRank = 2 },
                new PlanetSeed { Name = "Aurelia", DistanceRank = 3 },
                new PlanetSeed { Name = "Blue Horizon", DistanceRank = 4 },
                new PlanetSeed { Name = "Argus X", DistanceRank = 5 }
            ]
        }),
        Options.Create(new FleetOptions
        {
            ShuttleNames = ["Shuttle 1", "Shuttle 2", "Shuttle 3", "Shuttle 4"],
            HomePlanet = "Angel 1"
        }),
        NullLogger<DbSeeder>.Instance);

    [Fact]
    public async Task Seeding_creates_the_brief_s_world()
    {
        await using var db = NewContext();
        await SeederFor(db).SeedAsync();

        Assert.Equal(5, await db.Planets.CountAsync());
        Assert.Equal(4, await db.Shuttles.CountAsync());

        var planets = await db.Planets.OrderBy(p => p.DistanceRank).ToListAsync();
        Assert.Equal(
            ["Angel 1", "Boreth", "Aurelia", "Blue Horizon", "Argus X"],
            planets.Select(p => p.Name));

        var angel1 = planets[0];
        var shuttles = await db.Shuttles.ToListAsync();

        Assert.All(shuttles, s =>
        {
            Assert.Equal(ShuttleState.Idle, s.State);
            Assert.Equal(angel1.Id, s.CurrentPlanetId);
            Assert.Empty(s.Manifest);

            // The capacity survived the round trip through the owned-type mapping.
            Assert.Equal(20, s.Capacity.MaxLifeForms);
            Assert.Equal(4000m, s.Capacity.MaxWeightKg);
        });
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_the_fleet()
    {
        await using (var first = NewContext())
        {
            await SeederFor(first).SeedAsync();
        }

        // Second run = a server restart.
        await using var second = NewContext();
        await SeederFor(second).SeedAsync();

        Assert.Equal(5, await second.Planets.CountAsync());
        Assert.Equal(4, await second.Shuttles.CountAsync());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
