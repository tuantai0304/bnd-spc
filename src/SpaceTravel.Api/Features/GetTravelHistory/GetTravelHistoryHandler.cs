using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Data;

namespace SpaceTravel.Api.Features.GetTravelHistory;

public sealed class GetTravelHistoryHandler
{
    private readonly SpaceTravelDbContext _db;

    public GetTravelHistoryHandler(SpaceTravelDbContext db) => _db = db;

    public async Task<TravelHistoryPageResponse> HandleAsync(
        GetTravelHistoryRequest request,
        CancellationToken ct = default)
    {
        var planetNames = await _db.Planets
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var query = _db.TravelHistory.AsNoTracking();
        var totalCount = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(h => h.RecordedAtUtc)
            .ThenByDescending(h => h.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return new TravelHistoryPageResponse(
            request.Page,
            request.PageSize,
            totalCount,
            [.. entries.Select(e => GetTravelHistoryMapping.ToResponse(e, planetNames))]);
    }
}
