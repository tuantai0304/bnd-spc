using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Data;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Code.Simulation;

/// <summary>
/// One pass of the simulation: load the fleet, move it to <c>now</c>, persist what
/// changed. Extracted from the background service so tests can drive the world
/// forward deterministically instead of sleeping.
/// </summary>
public sealed class FleetAdvancer
{
    private readonly SpaceTravelDbContext _db;
    private readonly FleetRepository _fleetRepository;
    private readonly ILogger<FleetAdvancer> _logger;

    public FleetAdvancer(
        SpaceTravelDbContext db,
        FleetRepository fleetRepository,
        ILogger<FleetAdvancer> logger)
    {
        _db = db;
        _fleetRepository = fleetRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TravelHistoryEntry>> AdvanceAsync(
        DateTime now,
        CancellationToken ct = default)
    {
        var fleet = await _fleetRepository.LoadAsync(ct);
        var history = fleet.AdvanceTo(now);

        if (history.Count > 0)
        {
            _db.TravelHistory.AddRange(history);
        }

        if (!_db.ChangeTracker.HasChanges())
        {
            return history;
        }

        // The completed trip and its history row commit together, so a trip can
        // never be delivered without being recorded (BRD assumption #15).
        await _db.SaveChangesAsync(ct);

        foreach (var entry in history)
        {
            _logger.LogInformation(
                "Trip {RequestId} {Outcome}: {Count} life form(s), {Weight}kg to planet {Destination}.",
                entry.TravelRequestId, entry.Outcome, entry.LifeFormCount,
                entry.TotalWeightKg, entry.DestinationPlanetId);
        }

        return history;
    }
}
