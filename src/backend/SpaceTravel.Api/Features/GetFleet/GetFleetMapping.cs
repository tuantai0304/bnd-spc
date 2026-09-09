using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.GetFleet;

internal static class GetFleetMapping
{
    public static ShuttleResponse ToResponse(
        Shuttle shuttle,
        IReadOnlyDictionary<int, string> planetNames,
        DateTime now)
        => new(
            shuttle.Id,
            shuttle.Name,
            shuttle.State.ToString(),
            NameOf(shuttle.CurrentPlanetId, planetNames),
            NameOf(shuttle.FlyingToPlanetId, planetNames),
            NameOf(shuttle.PlannedDestinationId, planetNames),
            shuttle.ArrivesAtUtc,
            SecondsUntil(shuttle.ArrivesAtUtc, now),
            shuttle.UsedLifeForms,
            shuttle.UsedWeightKg,
            shuttle.RemainingLifeForms,
            shuttle.RemainingWeightKg,
            [.. shuttle.Manifest.Select(r => new ManifestEntryResponse(
                r.Id, r.Status.ToString(), r.LifeFormCount, r.TotalWeightKg))]);

    private static string? NameOf(int? planetId, IReadOnlyDictionary<int, string> names)
        => planetId is null ? null : names.GetValueOrDefault(planetId.Value);

    private static double? SecondsUntil(DateTime? arrivesAtUtc, DateTime now)
        => arrivesAtUtc is null
            ? null
            : Math.Max(0, (arrivesAtUtc.Value - now).TotalSeconds);
}
