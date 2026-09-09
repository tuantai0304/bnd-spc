using SpaceTravel.Api.Code.Dispatch;
using SpaceTravel.Api.Code.Time;
using SpaceTravel.Api.Data;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Features.CallShuttle;

/// <summary>
/// Orchestration only: take the gate, load the aggregate, let the Fleet decide,
/// record the outcome. Every rule about who can carry whom lives in the Fleet.
/// </summary>
public sealed class CallShuttleHandler
{
    private readonly SpaceTravelDbContext _db;
    private readonly FleetRepository _fleetRepository;
    private readonly IDispatchGate _gate;
    private readonly IClock _clock;
    private readonly ILogger<CallShuttleHandler> _logger;

    public CallShuttleHandler(
        SpaceTravelDbContext db,
        FleetRepository fleetRepository,
        IDispatchGate gate,
        IClock clock,
        ILogger<CallShuttleHandler> logger)
    {
        _db = db;
        _fleetRepository = fleetRepository;
        _gate = gate;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CallShuttleResponse> HandleAsync(
        CallShuttleRequest request,
        CancellationToken ct = default)
    {
        // Held across the whole read-decide-write, so two calls cannot both claim
        // the last seat on a shuttle (BRD edge case #4).
        using var _ = await _gate.AcquireAsync(ct);

        var now = _clock.UtcNow;
        var travelRequest = CallShuttleMapping.ToDomain(request, now);

        // Persist first so the request owns a real id. If the process died right
        // here the call would simply be found Queued and picked up by the next tick.
        _db.TravelRequests.Add(travelRequest);
        await _db.SaveChangesAsync(ct);

        var fleet = await _fleetRepository.LoadAsync(ct);
        var outcome = fleet.Dispatch(travelRequest, now);

        // A rejection is terminal, so it becomes history immediately — a dock that
        // keeps turning parties away is exactly what the brief wants to measure
        // (BRD assumption #12).
        if (outcome is DispatchOutcome.Rejected)
        {
            _db.TravelHistory.Add(TravelHistoryEntry.From(travelRequest, now));
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Call {RequestId} from planet {Origin} to {Destination} ({Count} life form(s), {Weight}kg): {Outcome}.",
            travelRequest.Id, request.OriginPlanetId, request.DestinationPlanetId,
            travelRequest.LifeFormCount, travelRequest.TotalWeightKg, outcome.GetType().Name);

        return CallShuttleMapping.ToResponse(travelRequest, outcome);
    }
}
