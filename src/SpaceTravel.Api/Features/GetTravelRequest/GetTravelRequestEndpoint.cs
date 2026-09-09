using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Code.Endpoints;
using SpaceTravel.Api.Code.Time;
using SpaceTravel.Api.Data;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.GetTravelRequest;

/// <summary>
/// Lets a passenger poll the call they made: Queued, Assigned, InTransit,
/// Completed or Rejected. Route parameter only, so no request body to validate.
/// </summary>
public sealed class GetTravelRequestEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapGet("/api/travel-requests/{id:int}", HandleAsync)
              .WithName("GetTravelRequest")
              .WithSummary("Check the status of one call.")
              .Produces<TravelRequestResponse>()
              .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        int id,
        SpaceTravelDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var request = await db.TravelRequests
            .AsNoTracking()
            .Include(r => r.LifeForms)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return Results.Problem(
                title: "Travel request not found.",
                detail: $"No call with id {id} exists.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var planetNames = await db.Planets
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        Shuttle? shuttle = null;
        if (request.AssignedShuttleId is { } shuttleId)
        {
            shuttle = await db.Shuttles
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == shuttleId, ct);
        }

        return Results.Ok(
            GetTravelRequestMapping.ToResponse(request, shuttle, planetNames, clock.UtcNow));
    }
}
