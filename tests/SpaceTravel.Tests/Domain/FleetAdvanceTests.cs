using SpaceTravel.Api.Domain;
using static SpaceTravel.Tests.Domain.FleetTestData;

namespace SpaceTravel.Tests.Domain;

/// <summary>
/// The shuttle state machine (BRD §3a) driven by a frozen clock, so travel time
/// is exact rather than wall-clock flaky.
/// </summary>
public class FleetAdvanceTests
{
    [Fact]
    public void A_shuttle_at_the_dock_loads_and_launches_on_the_next_tick()
    {
        var fleet = Fleet(HomeFleet());
        var request = Party(Angel1, Boreth);
        fleet.Dispatch(request, T0);

        var shuttle = fleet.Shuttles[0];

        // Dispatch commits but never moves anything.
        Assert.Equal(ShuttleState.Idle, shuttle.State);
        Assert.Equal(TravelRequestStatus.Assigned, request.Status);

        fleet.AdvanceTo(T0);

        Assert.Equal(ShuttleState.EnRoute, shuttle.State);
        Assert.Equal(TravelRequestStatus.InTransit, request.Status);
        Assert.Null(shuttle.CurrentPlanetId);
        Assert.Equal(Boreth, shuttle.FlyingToPlanetId);

        // Angel 1 -> Boreth is one rank step.
        Assert.Equal(T0 + BaseLeg, shuttle.ArrivesAtUtc);
    }

    [Fact]
    public void A_shuttle_stays_en_route_until_its_flight_time_elapses()
    {
        var fleet = Fleet(HomeFleet());
        fleet.Dispatch(Party(Angel1, ArgusX), T0);
        fleet.AdvanceTo(T0);

        var shuttle = fleet.Shuttles[0];

        // Angel 1 -> Argus X is 4 rank steps = 12s.
        fleet.AdvanceTo(T0 + TimeSpan.FromSeconds(11));
        Assert.Equal(ShuttleState.EnRoute, shuttle.State);

        fleet.AdvanceTo(T0 + TimeSpan.FromSeconds(12));
        Assert.Equal(ShuttleState.Idle, shuttle.State);
        Assert.Equal(ArgusX, shuttle.CurrentPlanetId);
    }

    [Fact]
    public void Arrival_completes_every_party_aboard_and_records_one_history_row_each()
    {
        var fleet = Fleet(HomeFleet());

        var first = Party(Angel1, Boreth, count: 2);
        var second = Party(Angel1, Boreth, count: 3);
        fleet.Dispatch(first, T0);
        fleet.Dispatch(second, T0);

        fleet.AdvanceTo(T0);
        var history = fleet.AdvanceTo(T0 + BaseLeg);

        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal(TravelRequestStatus.Completed, h.Outcome));
        Assert.All(history, h => Assert.Equal(Boreth, h.DestinationPlanetId));
        Assert.Equal([2, 3], history.Select(h => h.LifeFormCount).Order());

        Assert.Equal(TravelRequestStatus.Completed, first.Status);
        Assert.Equal(TravelRequestStatus.Completed, second.Status);

        // The shuttle is free again and its manifest is empty.
        var shuttle = fleet.Shuttles[0];
        Assert.Equal(ShuttleState.Idle, shuttle.State);
        Assert.Empty(shuttle.Manifest);
        Assert.Null(shuttle.PlannedDestinationId);

        // But each request still knows who flew it.
        Assert.Equal(shuttle.Id, first.AssignedShuttleId);
        Assert.Equal(shuttle.Id, second.AssignedShuttleId);
    }

    [Fact]
    public void History_is_only_emitted_once_per_trip()
    {
        var fleet = Fleet(HomeFleet());
        fleet.Dispatch(Party(Angel1, Boreth), T0);

        fleet.AdvanceTo(T0);
        Assert.Single(fleet.AdvanceTo(T0 + BaseLeg));

        // Ticking again must not re-record the same delivery.
        Assert.Empty(fleet.AdvanceTo(T0 + (2 * BaseLeg)));
        Assert.Empty(fleet.AdvanceTo(T0 + (3 * BaseLeg)));
    }

    // ---- Repositioning legs (BRD §3a's empty leg) ----------------------------

    [Fact]
    public void A_shuttle_elsewhere_flies_empty_to_the_dock_then_carries_the_party()
    {
        // The only shuttle is parked at Argus X; the call comes from Angel 1.
        var fleet = Fleet(ShuttlesAt(ArgusX));
        var request = Party(Angel1, Boreth);
        fleet.Dispatch(request, T0);

        var shuttle = fleet.Shuttles[0];

        // Leg 1: empty, Argus X -> Angel 1, 4 rank steps.
        fleet.AdvanceTo(T0);
        Assert.Equal(ShuttleState.EnRoute, shuttle.State);
        Assert.Equal(Angel1, shuttle.FlyingToPlanetId);
        Assert.Equal(TravelRequestStatus.Assigned, request.Status); // not aboard yet
        Assert.Equal(T0 + (4 * BaseLeg), shuttle.ArrivesAtUtc);

        // Leg 2: loaded, Angel 1 -> Boreth, 1 rank step.
        var atPickup = T0 + (4 * BaseLeg);
        var noHistoryYet = fleet.AdvanceTo(atPickup);
        Assert.Empty(noHistoryYet);
        Assert.Equal(ShuttleState.EnRoute, shuttle.State);
        Assert.Equal(Boreth, shuttle.FlyingToPlanetId);
        Assert.Equal(TravelRequestStatus.InTransit, request.Status);

        var history = fleet.AdvanceTo(atPickup + BaseLeg);
        Assert.Single(history);
        Assert.Equal(Boreth, shuttle.CurrentPlanetId);
    }

    [Fact]
    public void A_party_can_join_a_shuttle_that_is_still_flying_in_to_collect()
    {
        var fleet = Fleet(ShuttlesAt(ArgusX));

        var first = Party(Angel1, Boreth, count: 2);
        fleet.Dispatch(first, T0);
        fleet.AdvanceTo(T0); // shuttle sets off empty toward Angel 1

        Assert.Equal(ShuttleState.EnRoute, fleet.Shuttles[0].State);

        // A second party at Angel 1 wanting the same trip joins mid-approach.
        var second = Party(Angel1, Boreth, count: 3);
        var outcome = fleet.Dispatch(second, T0 + BaseLeg);

        Assert.IsType<DispatchOutcome.Assigned>(outcome);
        Assert.Equal(5, fleet.Shuttles[0].UsedLifeForms);

        // Both are carried on the one trip.
        var atPickup = T0 + (4 * BaseLeg);
        fleet.AdvanceTo(atPickup);
        var history = fleet.AdvanceTo(atPickup + BaseLeg);

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void Nothing_moves_when_there_is_nothing_to_do()
    {
        var fleet = Fleet(HomeFleet());

        Assert.Empty(fleet.AdvanceTo(T0));
        Assert.Empty(fleet.AdvanceTo(T0 + TimeSpan.FromHours(1)));
        Assert.All(fleet.Shuttles, s =>
        {
            Assert.Equal(ShuttleState.Idle, s.State);
            Assert.Equal(Angel1, s.CurrentPlanetId);
        });
    }
}
