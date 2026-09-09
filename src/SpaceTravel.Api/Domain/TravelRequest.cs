namespace SpaceTravel.Api.Domain;

/// <summary>
/// One "call" for a shuttle: a party of one or more life forms travelling
/// together from their dock to one destination (BRD assumption #3). One-way —
/// a return leg is a separate call (assumption #5).
/// </summary>
public sealed class TravelRequest
{
    private readonly List<LifeForm> _lifeForms = [];

    public int Id { get; private set; }
    public int OriginPlanetId { get; private set; }
    public int DestinationPlanetId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public TravelRequestStatus Status { get; private set; }
    public int? AssignedShuttleId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }

    public IReadOnlyList<LifeForm> LifeForms => _lifeForms;

    /// <summary>Derived, never stored — it cannot drift from the manifest.</summary>
    public int LifeFormCount => _lifeForms.Count;

    /// <summary>Derived, never stored — it cannot drift from the manifest.</summary>
    public decimal TotalWeightKg => _lifeForms.Sum(lf => lf.WeightKg);

    private TravelRequest(
        int originPlanetId,
        int destinationPlanetId,
        IEnumerable<LifeForm> lifeForms,
        DateTime requestedAtUtc)
    {
        OriginPlanetId = originPlanetId;
        DestinationPlanetId = destinationPlanetId;
        RequestedAtUtc = requestedAtUtc;
        Status = TravelRequestStatus.Queued;
        _lifeForms.AddRange(lifeForms);
    }

    /// <summary>EF materialisation only.</summary>
    private TravelRequest() { }

    public static TravelRequest Create(
        int originPlanetId,
        int destinationPlanetId,
        IEnumerable<LifeForm> lifeForms,
        DateTime requestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(lifeForms);

        var party = lifeForms.ToList();

        if (party.Count == 0)
        {
            throw new ArgumentException(
                "A travel request must carry at least one life form.", nameof(lifeForms));
        }

        // BRD business rule 4 / assumption #11: you cannot call a shuttle to the
        // planet you are already standing on.
        if (originPlanetId == destinationPlanetId)
        {
            throw new ArgumentException(
                "Origin and destination must be different planets.", nameof(destinationPlanetId));
        }

        return new TravelRequest(originPlanetId, destinationPlanetId, party, requestedAtUtc);
    }

    /// <summary>True while the request still needs the dispatcher's attention.</summary>
    public bool IsActive => !Status.IsTerminal();

    // ---- State transitions. Internal: only Fleet may move a request. ----------

    internal void MarkAssigned(int shuttleId)
    {
        if (Status is not (TravelRequestStatus.Queued or TravelRequestStatus.Assigned))
        {
            throw new InvalidOperationException(
                $"Cannot assign a request in state {Status}.");
        }

        Status = TravelRequestStatus.Assigned;
        AssignedShuttleId = shuttleId;
    }

    internal void MarkInTransit()
    {
        if (Status != TravelRequestStatus.Assigned)
        {
            throw new InvalidOperationException(
                $"Only an assigned request can depart; this one is {Status}.");
        }

        Status = TravelRequestStatus.InTransit;
    }

    internal void MarkCompleted(DateTime completedAtUtc)
    {
        if (Status != TravelRequestStatus.InTransit)
        {
            throw new InvalidOperationException(
                $"Only an in-transit request can complete; this one is {Status}.");
        }

        Status = TravelRequestStatus.Completed;
        CompletedAtUtc = completedAtUtc;
    }

    internal void MarkRejected(string reason, DateTime rejectedAtUtc)
    {
        if (Status.IsTerminal())
        {
            throw new InvalidOperationException(
                $"Cannot reject a request already in terminal state {Status}.");
        }

        Status = TravelRequestStatus.Rejected;
        RejectionReason = reason;
        CompletedAtUtc = rejectedAtUtc;
    }

    internal void ReturnToQueue()
    {
        Status = TravelRequestStatus.Queued;
        AssignedShuttleId = null;
    }
}
