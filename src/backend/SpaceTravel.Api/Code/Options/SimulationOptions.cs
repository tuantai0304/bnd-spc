namespace SpaceTravel.Api.Code.Options;

public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    /// <summary>
    /// Set false in integration tests so the background loop never races the
    /// assertions — tests drive Fleet.AdvanceTo themselves.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the tick service checks for arrivals and drains the queue.</summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Travel time for one step of distance rank. A leg costs
    /// max(1, |rank(from) - rank(to)|) x this. Ordinal rank is the only
    /// distance data the brief provides.
    /// </summary>
    public TimeSpan BaseLegDuration { get; init; } = TimeSpan.FromSeconds(3);
}
