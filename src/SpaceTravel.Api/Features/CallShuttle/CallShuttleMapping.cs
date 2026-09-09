using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.CallShuttle;

/// <summary>Request → domain, domain → response. Owned by this slice alone.</summary>
internal static class CallShuttleMapping
{
    public static TravelRequest ToDomain(CallShuttleRequest request, DateTime now)
        => TravelRequest.Create(
            request.OriginPlanetId,
            request.DestinationPlanetId,
            request.LifeForms.Select(lf => LifeForm.Create(lf.Species, lf.WeightKg)),
            now);

    public static CallShuttleResponse ToResponse(TravelRequest request, DispatchOutcome outcome)
    {
        var response = new CallShuttleResponse(
            request.Id,
            request.Status.ToString(),
            OutcomeName(outcome),
            request.LifeFormCount,
            request.TotalWeightKg);

        return outcome switch
        {
            DispatchOutcome.Assigned a => response with
            {
                ShuttleId = a.ShuttleId,
                ShuttleName = a.ShuttleName
            },
            DispatchOutcome.Queued q => response with { QueuePosition = q.PositionInQueue },
            DispatchOutcome.Rejected r => response with { RejectionReason = r.Reason },
            _ => response
        };
    }

    private static string OutcomeName(DispatchOutcome outcome) => outcome switch
    {
        DispatchOutcome.Assigned => "Assigned",
        DispatchOutcome.Queued => "Queued",
        DispatchOutcome.Rejected => "Rejected",
        _ => "Unknown"
    };
}
