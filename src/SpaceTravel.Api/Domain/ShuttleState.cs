namespace SpaceTravel.Api.Domain;

/// <summary>
/// Exactly three states (BRD §3a). Whether an En Route shuttle is carrying
/// passengers or repositioning empty is an attribute of its manifest, not a
/// fourth state.
/// </summary>
public enum ShuttleState
{
    /// <summary>Stationed at <see cref="Shuttle.CurrentPlanetId"/> and available.</summary>
    Idle = 0,

    /// <summary>Travelling; arrives at <see cref="Shuttle.ArrivesAtUtc"/>.</summary>
    EnRoute = 1,

    /// <summary>Touched down this tick; unload/pickup is processed, then it settles to Idle.</summary>
    Arrived = 2
}
