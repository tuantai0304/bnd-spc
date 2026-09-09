using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.GetTravelRequest;

internal static class GetTravelRequestMapping
{
    public static TravelRequestResponse ToResponse(
        TravelRequest request,
        Shuttle? shuttle,
        IReadOnlyDictionary<int, string> planetNames,
        DateTime now)
    {
        // An ETA is only meaningful while the party is actually flying somewhere.
        var arrivesAtUtc = request.Status == TravelRequestStatus.InTransit
            ? shuttle?.ArrivesAtUtc
            : null;

        return new TravelRequestResponse(
            request.Id,
            request.Status.ToString(),
            planetNames.GetValueOrDefault(request.OriginPlanetId, "Unknown"),
            planetNames.GetValueOrDefault(request.DestinationPlanetId, "Unknown"),
            request.LifeFormCount,
            request.TotalWeightKg,
            [.. request.LifeForms.Select(lf => new LifeFormResponse(lf.Species, lf.WeightKg))],
            request.RequestedAtUtc,
            request.CompletedAtUtc,
            request.AssignedShuttleId,
            shuttle?.Name,
            arrivesAtUtc,
            arrivesAtUtc is null ? null : Math.Max(0, (arrivesAtUtc.Value - now).TotalSeconds),
            request.RejectionReason);
    }
}
