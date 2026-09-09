namespace SpaceTravel.Api.Features.GetTravelRequest;

public sealed record TravelRequestResponse(
    int Id,
    string Status,
    string Origin,
    string Destination,
    int LifeFormCount,
    decimal TotalWeightKg,
    IReadOnlyList<LifeFormResponse> LifeForms,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    int? ShuttleId = null,
    string? ShuttleName = null,
    DateTime? EstimatedArrivalUtc = null,
    double? SecondsUntilArrival = null,
    string? RejectionReason = null);

public sealed record LifeFormResponse(string Species, decimal WeightKg);
