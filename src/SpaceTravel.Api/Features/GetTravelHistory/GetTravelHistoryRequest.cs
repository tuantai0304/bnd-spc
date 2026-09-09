namespace SpaceTravel.Api.Features.GetTravelHistory;

public sealed record GetTravelHistoryRequest(int Page = 1, int PageSize = 25);

public sealed record TravelHistoryPageResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<TravelHistoryItemResponse> Items);

public sealed record TravelHistoryItemResponse(
    int Id,
    int TravelRequestId,
    string Origin,
    string Destination,
    int LifeFormCount,
    decimal TotalWeightKg,
    string Outcome,
    int? ShuttleId,
    DateTime RequestedAtUtc,
    DateTime RecordedAtUtc,
    string? RejectionReason);
