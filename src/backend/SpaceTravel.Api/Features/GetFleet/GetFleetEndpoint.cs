using SpaceTravel.Api.Code.Endpoints;

namespace SpaceTravel.Api.Features.GetFleet;

public sealed class GetFleetEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapGet("/api/shuttles", HandleAsync)
              .WithName("GetFleet")
              .WithSummary("Live state of every shuttle: location, trip, manifest and spare capacity.")
              .Produces<IReadOnlyList<ShuttleResponse>>();

    private static async Task<IResult> HandleAsync(GetFleetHandler handler, CancellationToken ct)
        => Results.Ok(await handler.HandleAsync(ct));
}
