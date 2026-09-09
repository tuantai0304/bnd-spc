using SpaceTravel.Api.Domain;
using static SpaceTravel.Tests.Domain.FleetTestData;

namespace SpaceTravel.Tests.Domain;

/// <summary>
/// The "smarts to be efficient when picking up passengers", rule by rule.
/// </summary>
public class FleetDispatchTests
{
    // ---- Rule 0: reject the impossible (BRD edge case #2) --------------------

    [Fact]
    public void A_party_too_numerous_for_any_shuttle_is_rejected_not_queued()
    {
        var fleet = Fleet(HomeFleet());
        var request = Party(Angel1, ArgusX, count: 21, weightEachKg: 1m);

        var outcome = fleet.Dispatch(request, T0);

        Assert.IsType<DispatchOutcome.Rejected>(outcome);
        Assert.Equal(TravelRequestStatus.Rejected, request.Status);
        Assert.Empty(fleet.Queue);
    }

    [Fact]
    public void A_single_life_form_heavier_than_any_shuttle_is_rejected()
    {
        var fleet = Fleet(HomeFleet());
        var request = Party(Angel1, ArgusX, count: 1, weightEachKg: 5000m);

        var outcome = fleet.Dispatch(request, T0);

        var rejected = Assert.IsType<DispatchOutcome.Rejected>(outcome);
        Assert.Contains("4000", rejected.Reason);
    }

    // ---- Rule 1: batch onto a shuttle already going this way -----------------

    [Fact]
    public void A_second_call_for_the_same_trip_batches_onto_the_same_shuttle()
    {
        var fleet = Fleet(HomeFleet());

        var first = Party(Angel1, ArgusX, count: 2);
        var second = Party(Angel1, ArgusX, count: 3);

        var firstOutcome = Assert.IsType<DispatchOutcome.Assigned>(fleet.Dispatch(first, T0));
        var secondOutcome = Assert.IsType<DispatchOutcome.Assigned>(fleet.Dispatch(second, T0));

        // One launch instead of two — the whole point of the fuel requirement.
        Assert.Equal(firstOutcome.ShuttleId, secondOutcome.ShuttleId);
        Assert.Equal(5, fleet.Shuttles[0].UsedLifeForms);
    }

    [Fact]
    public void A_call_to_a_different_destination_does_not_batch()
    {
        var fleet = Fleet(HomeFleet());

        var toArgus = Assert.IsType<DispatchOutcome.Assigned>(
            fleet.Dispatch(Party(Angel1, ArgusX), T0));
        var toBoreth = Assert.IsType<DispatchOutcome.Assigned>(
            fleet.Dispatch(Party(Angel1, Boreth), T0));

        Assert.NotEqual(toArgus.ShuttleId, toBoreth.ShuttleId);
    }

    [Fact]
    public void Batching_still_respects_both_capacity_caps()
    {
        var fleet = Fleet(HomeFleet());

        // 18 of the 20 seats taken.
        fleet.Dispatch(Party(Angel1, ArgusX, count: 18, weightEachKg: 10m), T0);

        // 3 more would be 21 — too many for that shuttle, so it takes another.
        var outcome = Assert.IsType<DispatchOutcome.Assigned>(
            fleet.Dispatch(Party(Angel1, ArgusX, count: 3, weightEachKg: 10m), T0));

        Assert.NotEqual(1, outcome.ShuttleId);
        Assert.Equal(18, fleet.Shuttles[0].UsedLifeForms);
        Assert.Equal(3, fleet.Shuttles[1].UsedLifeForms);
    }

    /// <summary>BRD assumption #10: a call is never split across shuttles.</summary>
    [Fact]
    public void A_party_is_never_split_across_two_shuttles()
    {
        var fleet = Fleet(HomeFleet());

        fleet.Dispatch(Party(Angel1, ArgusX, count: 19, weightEachKg: 10m), T0);
        fleet.Dispatch(Party(Angel1, ArgusX, count: 4, weightEachKg: 10m), T0);

        // The party of 4 went somewhere whole; nobody carries a fragment of it.
        Assert.All(fleet.Shuttles, s => Assert.True(s.UsedLifeForms is 0 or 19 or 4));
        Assert.Equal(23, fleet.Shuttles.Sum(s => s.UsedLifeForms));
    }

    // ---- Rule 2: nearest idle shuttle ----------------------------------------

    [Fact]
    public void The_nearest_idle_shuttle_wins()
    {
        // Shuttle 1 at Argus X (rank 5), 2 at Aurelia (3), 3 at Boreth (2), 4 at Angel 1 (1).
        var fleet = Fleet(ShuttlesAt(ArgusX, Aurelia, Boreth, Angel1));

        // A call from Boreth: shuttle 3 is already standing there.
        var outcome = Assert.IsType<DispatchOutcome.Assigned>(
            fleet.Dispatch(Party(Boreth, ArgusX), T0));

        Assert.Equal(3, outcome.ShuttleId);
    }

    [Fact]
    public void When_nobody_is_at_the_dock_the_closest_one_is_sent()
    {
        // 1 at Angel 1 (rank 1), 2 at Argus X (5), 3 at Blue Horizon (4), 4 at Argus X (5).
        var fleet = Fleet(ShuttlesAt(Angel1, ArgusX, BlueHorizon, ArgusX));

        // Calling from Aurelia (rank 3): distances are 2, 2, 1, 2 — shuttle 3 is nearest.
        var outcome = Assert.IsType<DispatchOutcome.Assigned>(
            fleet.Dispatch(Party(Aurelia, Angel1), T0));

        Assert.Equal(3, outcome.ShuttleId);
    }

    [Fact]
    public void Ties_are_broken_by_the_lowest_shuttle_id_so_dispatch_is_deterministic()
    {
        var fleet = Fleet(HomeFleet());

        var outcome = Assert.IsType<DispatchOutcome.Assigned>(
            fleet.Dispatch(Party(Angel1, ArgusX), T0));

        Assert.Equal(1, outcome.ShuttleId);
    }

    // ---- Rule 3: queue (BRD assumption #13) ----------------------------------

    [Fact]
    public void When_every_shuttle_is_committed_the_call_is_queued()
    {
        var fleet = Fleet(HomeFleet());

        // Four calls to four different destinations occupy all four shuttles.
        foreach (var destination in new[] { Boreth, Aurelia, BlueHorizon, ArgusX })
        {
            Assert.IsType<DispatchOutcome.Assigned>(
                fleet.Dispatch(Party(Angel1, destination), T0));
        }

        var fifth = Party(Angel1, Boreth, count: 20, weightEachKg: 10m);
        var outcome = fleet.Dispatch(fifth, T0);

        var queued = Assert.IsType<DispatchOutcome.Queued>(outcome);
        Assert.Equal(1, queued.PositionInQueue);
        Assert.Equal(TravelRequestStatus.Queued, fifth.Status);
        Assert.Single(fleet.Queue);
    }

    [Fact]
    public void A_queued_call_is_picked_up_once_a_shuttle_frees_up()
    {
        var fleet = Fleet(HomeFleet());

        foreach (var destination in new[] { Boreth, Aurelia, BlueHorizon, ArgusX })
        {
            fleet.Dispatch(Party(Angel1, destination), T0);
        }

        var waiting = Party(Angel1, Boreth, count: 20, weightEachKg: 10m);
        Assert.IsType<DispatchOutcome.Queued>(fleet.Dispatch(waiting, T0));

        // Let the fleet run: Boreth is 1 rank step away, so shuttle 1 is back first.
        fleet.AdvanceTo(T0);
        fleet.AdvanceTo(T0 + TimeSpan.FromSeconds(4));

        Assert.Empty(fleet.Queue);
        Assert.NotEqual(TravelRequestStatus.Queued, waiting.Status);
        Assert.NotNull(waiting.AssignedShuttleId);
    }
}
