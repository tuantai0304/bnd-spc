using SpaceTravel.Api.Code.Endpoints;
using SpaceTravel.Api.Code.Validation;

namespace SpaceTravel.Api.Features.CallShuttle;

public sealed class CallShuttleEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
        => app.MapPost("/api/travel-requests", HandleAsync)
              .WithValidation<CallShuttleRequest>()
              .WithName("CallShuttle")
              .WithSummary("Call a shuttle from a space dock to another planet.")
              .Produces<CallShuttleResponse>(StatusCodes.Status201Created);

    private static async Task<IResult> HandleAsync(
        CallShuttleRequest request,
        CallShuttleHandler handler,
        CancellationToken ct)
    {
        var response = await handler.HandleAsync(request, ct);

        // Created even when the dispatcher rejected the party: the call itself is a
        // real, retrievable record, and its outcome is in the body.
        return Results.Created($"/api/travel-requests/{response.TravelRequestId}", response);
    }
}
