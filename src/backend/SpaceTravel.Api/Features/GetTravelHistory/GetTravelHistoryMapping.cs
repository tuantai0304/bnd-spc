using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.GetTravelHistory;

internal static class GetTravelHistoryMapping
{
    public static TravelHistoryItemResponse ToResponse(
        TravelHistoryEntry entry,
        IReadOnlyDictionary<int, string> planetNames)
        => new(
            entry.Id,
            entry.TravelRequestId,
            planetNames.GetValueOrDefault(entry.OriginPlanetId, "Unknown"),
            planetNames.GetValueOrDefault(entry.DestinationPlanetId, "Unknown"),
            entry.LifeFormCount,
            entry.TotalWeightKg,
            entry.Outcome.ToString(),
            entry.ShuttleId,
            entry.RequestedAtUtc,
            entry.RecordedAtUtc,
            entry.RejectionReason);
}
