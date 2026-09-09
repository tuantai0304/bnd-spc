namespace SpaceTravel.Api.Domain;

/// <summary>
/// One of the federation's shuttles. Rich: it owns its own capacity maths and
/// state transitions, and all mutators are internal so only <see cref="Fleet"/>
/// can move it. Nothing outside the aggregate can overbook or teleport a shuttle.
/// </summary>
/// <remarks>
/// A shuttle's manifest is homogeneous: every request aboard shares one origin
/// and one destination. That is a deliberate simplification — the brief never
/// asks for multi-stop routing, and it keeps "is this shuttle going my way?"
/// a single comparison. See docs/assumptions.md.
/// </remarks>
public sealed class Shuttle
{
    private readonly List<TravelRequest> _manifest = [];

    public int Id { get; private set; }
    public string Name { get; private set; }
    public ShuttleState State { get; private set; }
    public Capacity Capacity { get; private set; }

    /// <summary>Where it is parked. Null while <see cref="ShuttleState.EnRoute"/>.</summary>
    public int? CurrentPlanetId { get; private set; }

    /// <summary>The planet it is physically flying to. Null unless En Route.</summary>
    public int? FlyingToPlanetId { get; private set; }

    /// <summary>The destination it is committed to serving for its manifest.</summary>
    public int? PlannedDestinationId { get; private set; }

    public DateTime? DepartedAtUtc { get; private set; }
    public DateTime? ArrivesAtUtc { get; private set; }

    /// <summary>Requests committed to this shuttle, whether aboard or awaiting pickup.</summary>
    public IReadOnlyList<TravelRequest> Manifest => _manifest;

    /// <summary>The dock this shuttle must collect from, derived from its manifest.</summary>
    public int? PlannedOriginId => _manifest.Count == 0 ? null : _manifest[0].OriginPlanetId;

    public int UsedLifeForms => _manifest.Sum(r => r.LifeFormCount);
    public decimal UsedWeightKg => _manifest.Sum(r => r.TotalWeightKg);
    public int RemainingLifeForms => Capacity.RemainingLifeForms(UsedLifeForms);
    public decimal RemainingWeightKg => Capacity.RemainingWeightKg(UsedWeightKg);
    public bool IsEmpty => _manifest.Count == 0;

    private Shuttle(int id, string name, int homePlanetId, Capacity capacity)
    {
        Id = id;
        Name = name;
        CurrentPlanetId = homePlanetId;
        Capacity = capacity;
        State = ShuttleState.Idle;
    }

    /// <summary>EF materialisation only.</summary>
    private Shuttle()
    {
        Name = string.Empty;
        Capacity = null!;
    }

    /// <param name="id">
    /// Leave 0 in production so the database assigns it. Tests pass an explicit
    /// id to build a fleet without touching a database.
    /// </param>
    public static Shuttle Create(string name, int homePlanetId, Capacity capacity, int id = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A shuttle must have a name.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(capacity);

        return new Shuttle(id, name.Trim(), homePlanetId, capacity);
    }

    /// <summary>
    /// Rehydrates the manifest from storage. The manifest is "who is aboard right
    /// now", derived from the active requests pointing at this shuttle — it is not
    /// an owned collection, so completing a trip never erases a request's record of
    /// which shuttle carried it.
    /// </summary>
    internal void RestoreManifest(IEnumerable<TravelRequest> requests)
    {
        _manifest.Clear();
        _manifest.AddRange(requests);
    }

    /// <summary>
    /// Would this party fit alongside what is already committed? Checks both
    /// ceilings jointly — the brief's "20 life forms OR 4000kg".
    /// </summary>
    public bool CanAccept(TravelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Capacity.Accommodates(
            UsedLifeForms + request.LifeFormCount,
            UsedWeightKg + request.TotalWeightKg);
    }

    /// <summary>
    /// True when this shuttle is already committed to exactly this trip and could
    /// take one more party — the batching rule that conserves fuel.
    /// </summary>
    public bool IsServing(int originPlanetId, int destinationPlanetId)
        => PlannedDestinationId == destinationPlanetId && PlannedOriginId == originPlanetId;

    // ---- Transitions. Internal: only Fleet drives a shuttle. ------------------

    internal void Assign(TravelRequest request)
    {
        if (!IsEmpty && !IsServing(request.OriginPlanetId, request.DestinationPlanetId))
        {
            throw new InvalidOperationException(
                $"Shuttle {Name} is already serving a different trip.");
        }

        if (!CanAccept(request))
        {
            throw new InvalidOperationException(
                $"Shuttle {Name} cannot accept this party without breaching its capacity.");
        }

        _manifest.Add(request);
        PlannedDestinationId = request.DestinationPlanetId;
        request.MarkAssigned(Id);
    }

    /// <summary>Fly empty to the pickup dock. The manifest stays Assigned, not aboard.</summary>
    internal void DepartEmptyTo(int planetId, DateTime now, TimeSpan duration)
    {
        RequireStationary();
        BeginFlight(planetId, now, duration);
    }

    /// <summary>Take the waiting party aboard and fly to the destination.</summary>
    internal void DepartLoaded(DateTime now, TimeSpan duration)
    {
        RequireStationary();

        if (IsEmpty)
        {
            throw new InvalidOperationException($"Shuttle {Name} has nobody to carry.");
        }

        if (PlannedDestinationId is null)
        {
            throw new InvalidOperationException($"Shuttle {Name} has no destination.");
        }

        foreach (var request in _manifest)
        {
            request.MarkInTransit();
        }

        BeginFlight(PlannedDestinationId.Value, now, duration);
    }

    internal void Arrive(DateTime now)
    {
        if (State != ShuttleState.EnRoute)
        {
            throw new InvalidOperationException($"Shuttle {Name} is not en route.");
        }

        State = ShuttleState.Arrived;
        CurrentPlanetId = FlyingToPlanetId;
        FlyingToPlanetId = null;
        DepartedAtUtc = null;
        ArrivesAtUtc = null;
    }

    /// <summary>True when the shuttle has landed at the destination its party wanted.</summary>
    internal bool HasDeliveredParty
        => State == ShuttleState.Arrived
           && !IsEmpty
           && CurrentPlanetId == PlannedDestinationId;

    /// <summary>Sets everyone down and frees the shuttle. Returns the completed requests.</summary>
    internal IReadOnlyList<TravelRequest> Unload(DateTime now)
    {
        var delivered = _manifest.ToList();

        foreach (var request in delivered)
        {
            request.MarkCompleted(now);
        }

        _manifest.Clear();
        PlannedDestinationId = null;

        return delivered;
    }

    internal void Settle()
    {
        if (State == ShuttleState.Arrived)
        {
            State = ShuttleState.Idle;
        }
    }

    private void BeginFlight(int toPlanetId, DateTime now, TimeSpan duration)
    {
        State = ShuttleState.EnRoute;
        FlyingToPlanetId = toPlanetId;
        CurrentPlanetId = null;
        DepartedAtUtc = now;
        ArrivesAtUtc = now + duration;
    }

    private void RequireStationary()
    {
        if (State == ShuttleState.EnRoute)
        {
            throw new InvalidOperationException($"Shuttle {Name} is already en route.");
        }

        if (CurrentPlanetId is null)
        {
            throw new InvalidOperationException($"Shuttle {Name} has no known location.");
        }
    }
}
