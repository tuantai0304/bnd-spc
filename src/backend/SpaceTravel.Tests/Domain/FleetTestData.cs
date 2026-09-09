using SpaceTravel.Api.Domain;

namespace SpaceTravel.Tests.Domain;

/// <summary>
/// Builds the brief's world — 5 planets, 4 shuttles — entirely in memory.
/// No database, no host: these are the rules under test, nothing else.
/// </summary>
internal static class FleetTestData
{
    public const int Angel1 = 1;
    public const int Boreth = 2;
    public const int Aurelia = 3;
    public const int BlueHorizon = 4;
    public const int ArgusX = 5;

    public static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly TimeSpan BaseLeg = TimeSpan.FromSeconds(3);

    public static Capacity StandardCapacity => new(maxLifeForms: 20, maxWeightKg: 4000m);

    public static List<Planet> Planets() =>
    [
        Planet.Create("Angel 1", 1, Angel1),
        Planet.Create("Boreth", 2, Boreth),
        Planet.Create("Aurelia", 3, Aurelia),
        Planet.Create("Blue Horizon", 4, BlueHorizon),
        Planet.Create("Argus X", 5, ArgusX)
    ];

    /// <summary>Four shuttles, all parked at the planets given (ids 1..n).</summary>
    public static List<Shuttle> ShuttlesAt(params int[] planetIds)
        => [.. planetIds.Select((planetId, index) =>
            Shuttle.Create($"Shuttle {index + 1}", planetId, StandardCapacity, index + 1))];

    /// <summary>The default posture: all four idle at Angel 1.</summary>
    public static List<Shuttle> HomeFleet()
        => ShuttlesAt(Angel1, Angel1, Angel1, Angel1);

    public static Fleet Fleet(List<Shuttle> shuttles, params TravelRequest[] queued)
        => Api.Domain.Fleet.Create(Planets(), shuttles, queued, BaseLeg);

    /// <summary>A party of <paramref name="count"/> life forms of equal weight.</summary>
    public static TravelRequest Party(
        int origin,
        int destination,
        int count = 1,
        decimal weightEachKg = 80m,
        DateTime? at = null)
        => TravelRequest.Create(
            origin,
            destination,
            Enumerable.Range(0, count).Select(i => LifeForm.Create($"Species{i}", weightEachKg)),
            at ?? T0);
}
