namespace SpaceTravel.Api.Features.CallShuttle;

/// <summary>
/// A passenger at a space dock calling for a shuttle. The party travels together
/// to one destination (BRD assumption #3).
/// </summary>
public sealed record CallShuttleRequest(
    int OriginPlanetId,
    int DestinationPlanetId,
    IReadOnlyList<CallShuttleLifeForm> LifeForms);

public sealed record CallShuttleLifeForm(string Species, decimal WeightKg);
