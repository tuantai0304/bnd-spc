using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTravel.Api.Code.Options;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data;

/// <summary>
/// Rebuilds the <see cref="Fleet"/> aggregate from its rows. Because every child
/// it hands to the Fleet is change-tracked, whatever the domain mutates is saved
/// by a plain SaveChangesAsync — there is no mapping or diffing step.
/// </summary>
public sealed class FleetRepository
{
    private readonly SpaceTravelDbContext _db;
    private readonly SimulationOptions _simulation;

    public FleetRepository(SpaceTravelDbContext db, IOptions<SimulationOptions> simulation)
    {
        _db = db;
        _simulation = simulation.Value;
    }

    public async Task<Fleet> LoadAsync(CancellationToken ct = default)
    {
        var planets = await _db.Planets
            .OrderBy(p => p.DistanceRank)
            .ToListAsync(ct);

        var shuttles = await _db.Shuttles
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        // Every call still in play, with its party so the capacity maths is exact.
        var active = await _db.TravelRequests
            .Include(r => r.LifeForms)
            .Where(r => r.Status == TravelRequestStatus.Queued
                        || r.Status == TravelRequestStatus.Assigned
                        || r.Status == TravelRequestStatus.InTransit)
            .OrderBy(r => r.RequestedAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        // Aboard-or-committed requests become manifests; the rest are the queue.
        // Splitting here rather than through a navigation is what lets a completed
        // request keep its AssignedShuttleId.
        var byShuttle = active
            .Where(r => r.Status != TravelRequestStatus.Queued && r.AssignedShuttleId is not null)
            .GroupBy(r => r.AssignedShuttleId!.Value)
            .ToDictionary(g => g.Key, IReadOnlyList<TravelRequest> (g) => [.. g]);

        foreach (var shuttle in shuttles)
        {
            shuttle.RestoreManifest(byShuttle.GetValueOrDefault(shuttle.Id, []));
        }

        var queued = active
            .Where(r => r.Status == TravelRequestStatus.Queued)
            .ToList();

        return Fleet.Create(planets, shuttles, queued, _simulation.BaseLegDuration);
    }
}
