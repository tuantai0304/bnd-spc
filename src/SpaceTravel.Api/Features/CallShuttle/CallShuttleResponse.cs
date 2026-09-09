namespace SpaceTravel.Api.Features.CallShuttle;

/// <summary>
/// What the dispatcher decided. Every call becomes a persisted request, so the
/// passenger always gets an id to poll — even when nothing could carry them.
/// </summary>
public sealed record CallShuttleResponse(
    int TravelRequestId,
    string Status,
    string Outcome,
    int LifeFormCount,
    decimal TotalWeightKg,
    int? ShuttleId = null,
    string? ShuttleName = null,
    int? QueuePosition = null,
    string? RejectionReason = null);
