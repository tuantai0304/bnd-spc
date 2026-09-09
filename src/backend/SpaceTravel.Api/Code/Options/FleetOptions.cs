namespace SpaceTravel.Api.Code.Options;

/// <summary>
/// The fleet. Size comes from configuration so "deploy more shuttles" — the
/// business goal behind persisting travel history — is a config change, not a
/// code change (BRD assumption #9).
/// </summary>
public sealed class FleetOptions
{
    public const string SectionName = "Fleet";

    public List<string> ShuttleNames { get; init; } = [];

    /// <summary>Name of the planet all shuttles are stationed at on first run.</summary>
    public string HomePlanet { get; init; } = "Angel 1";

    /// <summary>Max life forms per shuttle. Brief: 20.</summary>
    public int MaxLifeFormsPerShuttle { get; init; } = 20;

    /// <summary>Max total load per shuttle in kg. Brief: 4000.</summary>
    public decimal MaxWeightKgPerShuttle { get; init; } = 4000m;
}
