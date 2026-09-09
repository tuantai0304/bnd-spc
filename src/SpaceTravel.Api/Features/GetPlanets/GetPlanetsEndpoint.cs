using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Code.Endpoints;
using SpaceTravel.Api.Data;

namespace SpaceTravel.Api.Features.GetPlanets;

/// <summary>
/// The 5 space docks in distance order — what a front end needs to offer a
/// destination picker. No request body, so no validator.
/// </summary>
public sealed class GetPlanetsEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapGet("/api/planets", HandleAsync)
              .WithName("GetPlanets")
              .WithSummary("List the known planets and their space docks, nearest first.")
              .Produces<IReadOnlyList<PlanetResponse>>();

    private static async Task<IResult> HandleAsync(
        SpaceTravelDbContext db,
        CancellationToken ct)
    {
        var planets = await db.Planets
            .AsNoTracking()
            .OrderBy(p => p.DistanceRank)
            .Select(p => new PlanetResponse(p.Id, p.Name, p.DistanceRank))
            .ToListAsync(ct);

        return Results.Ok(planets);
    }
}

public sealed record PlanetResponse(int Id, string Name, int DistanceRank);
