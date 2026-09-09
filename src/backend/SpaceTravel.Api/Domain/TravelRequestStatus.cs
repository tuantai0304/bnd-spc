namespace SpaceTravel.Api.Domain;

public enum TravelRequestStatus
{
    /// <summary>No shuttle could take it yet; waiting FIFO (BRD assumption #13).</summary>
    Queued = 0,

    /// <summary>A shuttle is committed to it but has not departed with it aboard.</summary>
    Assigned = 1,

    /// <summary>Aboard a shuttle that is flying to the destination.</summary>
    InTransit = 2,

    /// <summary>Delivered. Terminal.</summary>
    Completed = 3,

    /// <summary>No shuttle could ever carry it. Terminal.</summary>
    Rejected = 4
}

public static class TravelRequestStatusExtensions
{
    public static bool IsTerminal(this TravelRequestStatus status)
        => status is TravelRequestStatus.Completed or TravelRequestStatus.Rejected;
}
