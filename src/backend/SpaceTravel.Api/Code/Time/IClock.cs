namespace SpaceTravel.Api.Code.Time;

/// <summary>
/// Abstracts "now" so the domain can be driven deterministically from tests.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
