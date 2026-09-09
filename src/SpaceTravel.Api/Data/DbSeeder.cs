using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTravel.Api.Code.Options;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data;

/// <summary>
/// Brings the database up to date and puts the world in it. Runs at startup so
/// "running the server should be easy" means exactly `dotnet run` — no scripts,
/// no manual migration step.
/// </summary>
public sealed class DbSeeder
{
    private readonly SpaceTravelDbContext _db;
    private readonly PlanetOptions _planets;
    private readonly FleetOptions _fleet;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(
        SpaceTravelDbContext db,
        IOptions<PlanetOptions> planets,
        IOptions<FleetOptions> fleet,
        ILogger<DbSeeder> logger)
    {
        _db = db;
        _planets = planets.Value;
        _fleet = fleet.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _db.Database.MigrateAsync(ct);

        await SeedPlanetsAsync(ct);
        await SeedShuttlesAsync(ct);
    }

    private async Task SeedPlanetsAsync(CancellationToken ct)
    {
        if (await _db.Planets.AnyAsync(ct))
        {
            return;
        }

        if (_planets.Known.Count == 0)
        {
            throw new InvalidOperationException(
                "No planets are configured. Populate the 'Planets:Known' section of appsettings.json.");
        }

        var planets = _planets.Known
            .Select(p => Planet.Create(p.Name, p.DistanceRank))
            .ToList();

        _db.Planets.AddRange(planets);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Seeded {Count} planets.", planets.Count);
    }

    private async Task SeedShuttlesAsync(CancellationToken ct)
    {
        if (await _db.Shuttles.AnyAsync(ct))
        {
            return;
        }

        if (_fleet.ShuttleNames.Count == 0)
        {
            throw new InvalidOperationException(
                "No shuttles are configured. Populate the 'Fleet:ShuttleNames' section of appsettings.json.");
        }

        var home = await _db.Planets.FirstOrDefaultAsync(p => p.Name == _fleet.HomePlanet, ct)
                   ?? await _db.Planets.OrderBy(p => p.DistanceRank).FirstAsync(ct);

        // A fresh Capacity per shuttle: it is an owned entity, so EF needs one
        // instance per owner rather than four owners sharing a single reference.
        var shuttles = _fleet.ShuttleNames
            .Select(name => Shuttle.Create(
                name,
                home.Id,
                new Capacity(_fleet.MaxLifeFormsPerShuttle, _fleet.MaxWeightKgPerShuttle)))
            .ToList();

        _db.Shuttles.AddRange(shuttles);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seeded {Count} shuttles at {Planet}, each carrying up to {MaxLifeForms} life forms or {MaxWeightKg}kg.",
            shuttles.Count,
            home.Name,
            _fleet.MaxLifeFormsPerShuttle,
            _fleet.MaxWeightKgPerShuttle);
    }
}
