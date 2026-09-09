namespace SpaceTravel.Api.Domain;

/// <summary>
/// The aggregate root. Every rule the brief cares about — the dual capacity cap,
/// the "smarts" that conserve fuel, the queue, the state machine — lives here and
/// only here. Handlers load a Fleet, call one method, and save; they never reach
/// into a Shuttle, which is why two concurrent calls cannot overbook one.
/// </summary>
public sealed class Fleet
{
    private readonly List<Planet> _planets;
    private readonly List<Shuttle> _shuttles;
    private readonly List<TravelRequest> _queue;
    private readonly TimeSpan _baseLegDuration;

    public IReadOnlyList<Planet> Planets => _planets;
    public IReadOnlyList<Shuttle> Shuttles => _shuttles;

    /// <summary>Calls waiting for a shuttle, oldest first (BRD assumption #13).</summary>
    public IReadOnlyList<TravelRequest> Queue => _queue;

    private Fleet(
        List<Planet> planets,
        List<Shuttle> shuttles,
        List<TravelRequest> queue,
        TimeSpan baseLegDuration)
    {
        _planets = planets;
        _shuttles = shuttles;
        _queue = queue;
        _baseLegDuration = baseLegDuration;
    }

    public static Fleet Create(
        IEnumerable<Planet> planets,
        IEnumerable<Shuttle> shuttles,
        IEnumerable<TravelRequest> queuedRequests,
        TimeSpan baseLegDuration)
    {
        ArgumentNullException.ThrowIfNull(planets);
        ArgumentNullException.ThrowIfNull(shuttles);
        ArgumentNullException.ThrowIfNull(queuedRequests);

        if (baseLegDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseLegDuration), baseLegDuration, "A leg must take positive time.");
        }

        var queue = queuedRequests
            .OrderBy(r => r.RequestedAtUtc)
            .ThenBy(r => r.Id)
            .ToList();

        return new Fleet(planets.ToList(), shuttles.ToList(), queue, baseLegDuration);
    }

    // =========================================================================
    //  Dispatch — the "smarts to be efficient when picking up passengers"
    // =========================================================================

    /// <summary>
    /// Decides who serves this call. Ordered rules, cheapest first:
    /// <list type="number">
    /// <item>Reject what no shuttle could ever carry.</item>
    /// <item>Batch onto a shuttle already committed to this exact trip — one
    ///       launch instead of two is the single biggest fuel saving available.</item>
    /// <item>Otherwise send the nearest idle shuttle, using ordinal planet
    ///       distance as the fuel proxy.</item>
    /// <item>Otherwise queue it.</item>
    /// </list>
    /// </summary>
    public DispatchOutcome Dispatch(TravelRequest request, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(request);

        // --- Rule 0: fail fast on the impossible (BRD edge case #2) -----------
        if (!_shuttles.Any(s => s.Capacity.Accommodates(request.LifeFormCount, request.TotalWeightKg)))
        {
            var reason =
                $"No shuttle can carry {request.LifeFormCount} life form(s) weighing " +
                $"{request.TotalWeightKg:0.##}kg. The largest shuttle takes " +
                $"{_shuttles.Max(s => s.Capacity.MaxLifeForms)} life forms or " +
                $"{_shuttles.Max(s => s.Capacity.MaxWeightKg):0.##}kg.";

            request.MarkRejected(reason, now);
            return new DispatchOutcome.Rejected(reason);
        }

        // --- Rule 1: batch onto a shuttle already going this way --------------
        var batchTarget = _shuttles
            .Where(s => s.IsServing(request.OriginPlanetId, request.DestinationPlanetId)
                        && WillCollectFrom(s, request.OriginPlanetId)
                        && s.CanAccept(request))
            // Fill the fullest one first, so we consolidate rather than spread.
            .OrderByDescending(s => s.UsedLifeForms)
            .ThenByDescending(s => s.UsedWeightKg)
            .ThenBy(s => s.Id)
            .FirstOrDefault();

        if (batchTarget is not null)
        {
            batchTarget.Assign(request);
            return new DispatchOutcome.Assigned(batchTarget.Id, batchTarget.Name);
        }

        // --- Rule 2: nearest idle shuttle -------------------------------------
        var nearestIdle = _shuttles
            .Where(s => s.IsEmpty && IsStationary(s) && s.CanAccept(request))
            .OrderBy(s => DistanceBetween(s.CurrentPlanetId!.Value, request.OriginPlanetId))
            .ThenBy(s => s.Id)
            .FirstOrDefault();

        if (nearestIdle is not null)
        {
            nearestIdle.Assign(request);
            return new DispatchOutcome.Assigned(nearestIdle.Id, nearestIdle.Name);
        }

        // --- Rule 3: everyone is busy; wait your turn -------------------------
        if (!_queue.Contains(request))
        {
            _queue.Add(request);
        }

        return new DispatchOutcome.Queued(_queue.IndexOf(request) + 1);
    }

    // =========================================================================
    //  Simulation — moves time forward
    // =========================================================================

    /// <summary>
    /// Advances every shuttle to <paramref name="now"/>: lands arrivals, unloads
    /// delivered parties, drains the queue onto freed shuttles, then launches
    /// whoever is ready. Returns the history rows the caller must persist.
    /// </summary>
    public IReadOnlyList<TravelHistoryEntry> AdvanceTo(DateTime now)
    {
        var history = new List<TravelHistoryEntry>();

        // 1. Land anything whose flight time has elapsed.
        foreach (var shuttle in _shuttles.Where(s =>
                     s.State == ShuttleState.EnRoute && s.ArrivesAtUtc <= now))
        {
            shuttle.Arrive(now);
        }

        // 2. Set down parties that reached their destination.
        foreach (var shuttle in _shuttles.Where(s => s.HasDeliveredParty))
        {
            foreach (var delivered in shuttle.Unload(now))
            {
                history.Add(TravelHistoryEntry.From(delivered, now));
            }
        }

        // 3. Arrived is transient; a shuttle either flies on or becomes available.
        foreach (var shuttle in _shuttles)
        {
            shuttle.Settle();
        }

        // 4. Now that shuttles have freed up, give the queue another chance.
        DrainQueue(now);

        // 5. Launch every stationary shuttle that has a party to move.
        foreach (var shuttle in _shuttles.Where(s => IsStationary(s) && !s.IsEmpty))
        {
            LaunchNextLeg(shuttle, now);
        }

        return history;
    }

    private void DrainQueue(DateTime now)
    {
        if (_queue.Count == 0)
        {
            return;
        }

        // Snapshot: Dispatch mutates _queue when a request stays queued.
        foreach (var waiting in _queue.ToList())
        {
            var outcome = Dispatch(waiting, now);

            if (outcome is not DispatchOutcome.Queued)
            {
                _queue.Remove(waiting);
            }
        }
    }

    private void LaunchNextLeg(Shuttle shuttle, DateTime now)
    {
        var from = shuttle.CurrentPlanetId!.Value;
        var pickup = shuttle.PlannedOriginId!.Value;
        var destination = shuttle.PlannedDestinationId!.Value;

        if (from == pickup)
        {
            // The party is standing right here — take them aboard and go.
            shuttle.DepartLoaded(now, LegDuration(from, destination));
        }
        else
        {
            // Fly empty to collect them (BRD §3a's repositioning leg).
            shuttle.DepartEmptyTo(pickup, now, LegDuration(from, pickup));
        }
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private static bool IsStationary(Shuttle shuttle)
        => shuttle.State is ShuttleState.Idle or ShuttleState.Arrived;

    /// <summary>
    /// True when the shuttle has not yet left the pickup dock — either it is
    /// parked there, or it is flying there empty to collect. Both are moments
    /// when one more party can still join the trip.
    /// </summary>
    private static bool WillCollectFrom(Shuttle shuttle, int originPlanetId)
        => (IsStationary(shuttle) && shuttle.CurrentPlanetId == originPlanetId)
           || (shuttle.State == ShuttleState.EnRoute && shuttle.FlyingToPlanetId == originPlanetId);

    private TimeSpan LegDuration(int fromPlanetId, int toPlanetId)
        => Math.Max(1, DistanceBetween(fromPlanetId, toPlanetId)) * _baseLegDuration;

    private int DistanceBetween(int fromPlanetId, int toPlanetId)
    {
        var from = PlanetById(fromPlanetId);
        var to = PlanetById(toPlanetId);
        return from.DistanceTo(to);
    }

    private Planet PlanetById(int id)
        => _planets.FirstOrDefault(p => p.Id == id)
           ?? throw new InvalidOperationException($"Unknown planet id {id}.");
}
