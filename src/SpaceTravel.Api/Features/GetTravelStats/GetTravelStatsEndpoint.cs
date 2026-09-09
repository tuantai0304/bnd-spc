using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Code.Endpoints;
using SpaceTravel.Api.Data;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.GetTravelStats;

/// <summary>
/// The brief's stated reason for persisting anything: "help the team to study
/// most traveled planets and deploy more shuttles in the future". Rejected calls
/// are reported alongside completed ones — a dock with many rejections is where
/// the next shuttle earns its keep.
/// </summary>
public sealed class GetTravelStatsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapGet("/api/travel-history/stats", HandleAsync)
              .WithName("GetTravelStats")
              .WithSummary("Trips per destination planet, most travelled first.")
              .Produces<IReadOnlyList<PlanetTravelStatsResponse>>();

    private static async Task<IResult> HandleAsync(
        SpaceTravelDbContext db,
        CancellationToken ct)
    {
        var planets = await db.Planets
            .AsNoTracking()
            .OrderBy(p => p.DistanceRank)
            .ToListAsync(ct);

        var grouped = await db.TravelHistory
            .AsNoTracking()
            .GroupBy(h => h.DestinationPlanetId)
            .Select(g => new
            {
                DestinationPlanetId = g.Key,
                Completed = g.Count(h => h.Outcome == TravelRequestStatus.Completed),
                Rejected = g.Count(h => h.Outcome == TravelRequestStatus.Rejected),
                LifeFormsDelivered = g
                    .Where(h => h.Outcome == TravelRequestStatus.Completed)
                    .Sum(h => h.LifeFormCount),
                WeightDeliveredKg = g
                    .Where(h => h.Outcome == TravelRequestStatus.Completed)
                    .Sum(h => h.TotalWeightKg)
            })
            .ToListAsync(ct);

        var byPlanet = grouped.ToDictionary(g => g.DestinationPlanetId);

        // Every planet appears, including those nobody has travelled to — a zero
        // is a finding, and leaving the row out would hide it.
        var stats = planets
            .Select(p =>
            {
                var row = byPlanet.GetValueOrDefault(p.Id);
                return new PlanetTravelStatsResponse(
                    p.Id,
                    p.Name,
                    p.DistanceRank,
                    row?.Completed ?? 0,
                    row?.Rejected ?? 0,
                    row?.LifeFormsDelivered ?? 0,
                    row?.WeightDeliveredKg ?? 0m);
            })
            .OrderByDescending(s => s.CompletedTrips)
            .ThenBy(s => s.DistanceRank)
            .ToList();

        return Results.Ok(stats);
    }
}

public sealed record PlanetTravelStatsResponse(
    int PlanetId,
    string Planet,
    int DistanceRank,
    int CompletedTrips,
    int RejectedCalls,
    int LifeFormsDelivered,
    decimal WeightDeliveredKg);
