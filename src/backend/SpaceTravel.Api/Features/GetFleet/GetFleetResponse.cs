namespace SpaceTravel.Api.Features.GetFleet;

/// <summary>
/// Live fleet state — the data behind the brief's "UI to illustrate how the
/// passenger pickup and shuttle system works".
/// </summary>
public sealed record ShuttleResponse(
    int Id,
    string Name,
    string State,
    string? CurrentPlanet,
    string? FlyingToPlanet,
    string? TripDestination,
    DateTime? ArrivesAtUtc,
    double? SecondsUntilArrival,
    int LifeFormsAboard,
    decimal WeightAboardKg,
    int RemainingLifeForms,
    decimal RemainingWeightKg,
    IReadOnlyList<ManifestEntryResponse> Manifest);

public sealed record ManifestEntryResponse(
    int TravelRequestId,
    string Status,
    int LifeFormCount,
    decimal TotalWeightKg);
