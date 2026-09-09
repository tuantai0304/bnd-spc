using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Code.Time;
using SpaceTravel.Api.Data;

namespace SpaceTravel.Api.Features.GetFleet;

public sealed class GetFleetHandler
{
    private readonly SpaceTravelDbContext _db;
    private readonly FleetRepository _fleetRepository;
    private readonly IClock _clock;

    public GetFleetHandler(SpaceTravelDbContext db, FleetRepository fleetRepository, IClock clock)
    {
        _db = db;
        _fleetRepository = fleetRepository;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ShuttleResponse>> HandleAsync(CancellationToken ct = default)
    {
        var planetNames = await _db.Planets
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        // The same aggregate the dispatcher works with, so what the UI shows and
        // what the Fleet decides can never disagree.
        var fleet = await _fleetRepository.LoadAsync(ct);
        var now = _clock.UtcNow;

        return [.. fleet.Shuttles.Select(s => GetFleetMapping.ToResponse(s, planetNames, now))];
    }
}
