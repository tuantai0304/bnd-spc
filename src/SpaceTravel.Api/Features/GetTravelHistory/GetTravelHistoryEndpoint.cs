using SpaceTravel.Api.Code.Endpoints;
using SpaceTravel.Api.Code.Validation;

namespace SpaceTravel.Api.Features.GetTravelHistory;

public sealed class GetTravelHistoryEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapGet("/api/travel-history", HandleAsync)
              .WithValidation<GetTravelHistoryRequest>()
              .WithName("GetTravelHistory")
              .WithSummary("Paged log of every call that completed or was rejected.")
              .Produces<TravelHistoryPageResponse>();

    private static async Task<IResult> HandleAsync(
        [AsParameters] GetTravelHistoryRequest request,
        GetTravelHistoryHandler handler,
        CancellationToken ct)
        => Results.Ok(await handler.HandleAsync(request, ct));
}
