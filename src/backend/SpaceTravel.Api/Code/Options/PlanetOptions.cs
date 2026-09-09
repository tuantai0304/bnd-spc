namespace SpaceTravel.Api.Code.Options;

/// <summary>
/// The known planets. Configurable rather than hardcoded (BRD assumption #9:
/// "scalable, future proofed") — the 5 in appsettings are today's defaults,
/// not a constraint of the model.
/// </summary>
public sealed class PlanetOptions
{
    public const string SectionName = "Planets";

    public List<PlanetSeed> Known { get; init; } = [];
}

public sealed class PlanetSeed
{
    public required string Name { get; init; }

    /// <summary>1 = closest to the space academy. Ordinal only — the brief gives no distances.</summary>
    public required int DistanceRank { get; init; }
}
