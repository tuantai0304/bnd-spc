namespace SpaceTravel.Api.Domain;

/// <summary>
/// The persisted record of a call that reached a terminal state. This is the
/// brief's actual business goal — "help the team to study most traveled planets
/// and deploy more shuttles" — so it deliberately records rejections too
/// (BRD assumption #12): a dock that keeps turning parties away is exactly where
/// the next shuttle should go.
/// </summary>
public sealed class TravelHistoryEntry
{
    public int Id { get; private set; }
    public int TravelRequestId { get; private set; }
    public int OriginPlanetId { get; private set; }
    public int DestinationPlanetId { get; private set; }
    public int LifeFormCount { get; private set; }
    public decimal TotalWeightKg { get; private set; }
    public int? ShuttleId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public TravelRequestStatus Outcome { get; private set; }
    public string? RejectionReason { get; private set; }

    private TravelHistoryEntry() { }

    /// <summary>
    /// Snapshots a request at the moment it completed or was rejected. Values are
    /// copied, not referenced, so history stays true even if the model changes later.
    /// </summary>
    public static TravelHistoryEntry From(TravelRequest request, DateTime recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Status.IsTerminal())
        {
            throw new InvalidOperationException(
                $"Only a completed or rejected request becomes history; this one is {request.Status}.");
        }

        return new TravelHistoryEntry
        {
            TravelRequestId = request.Id,
            OriginPlanetId = request.OriginPlanetId,
            DestinationPlanetId = request.DestinationPlanetId,
            LifeFormCount = request.LifeFormCount,
            TotalWeightKg = request.TotalWeightKg,
            ShuttleId = request.AssignedShuttleId,
            RequestedAtUtc = request.RequestedAtUtc,
            RecordedAtUtc = recordedAtUtc,
            Outcome = request.Status,
            RejectionReason = request.RejectionReason
        };
    }
}
