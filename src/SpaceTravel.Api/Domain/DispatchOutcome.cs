namespace SpaceTravel.Api.Domain;

/// <summary>
/// What the dispatcher decided. A closed hierarchy so callers must handle every case.
/// </summary>
public abstract record DispatchOutcome
{
    private DispatchOutcome() { }

    /// <summary>A shuttle is committed to this party.</summary>
    public sealed record Assigned(int ShuttleId, string ShuttleName) : DispatchOutcome;

    /// <summary>Every shuttle is busy or full; the tick will retry as capacity frees up.</summary>
    public sealed record Queued(int PositionInQueue) : DispatchOutcome;

    /// <summary>No shuttle in the fleet could ever carry this party.</summary>
    public sealed record Rejected(string Reason) : DispatchOutcome;
}
