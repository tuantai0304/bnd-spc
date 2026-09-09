using SpaceTravel.Api.Domain;
using static SpaceTravel.Tests.Domain.FleetTestData;

namespace SpaceTravel.Tests.Domain;

/// <summary>
/// Phase 2 verification: the domain works standing on its own.
/// The full rule-by-rule suite lands in Phase 6.
/// </summary>
public class FleetSmokeTests
{
    [Fact]
    public void Dispatch_assigns_a_shuttle_to_the_first_call()
    {
        var fleet = Fleet(HomeFleet());

        var outcome = fleet.Dispatch(Party(Angel1, ArgusX), T0);

        var assigned = Assert.IsType<DispatchOutcome.Assigned>(outcome);
        Assert.Equal(1, assigned.ShuttleId);
    }

    [Fact]
    public void Dispatch_rejects_a_party_no_shuttle_could_ever_carry()
    {
        var fleet = Fleet(HomeFleet());

        var outcome = fleet.Dispatch(Party(Angel1, ArgusX, count: 21), T0);

        var rejected = Assert.IsType<DispatchOutcome.Rejected>(outcome);
        Assert.Contains("21 life form", rejected.Reason);
    }

    [Fact]
    public void A_call_travels_from_assignment_through_to_completed_history()
    {
        var fleet = Fleet(HomeFleet());
        var request = Party(Angel1, ArgusX);
        fleet.Dispatch(request, T0);

        // Tick 1: the shuttle loads and launches. Angel 1 -> Argus X is 4 rank steps.
        var nothingYet = fleet.AdvanceTo(T0);
        Assert.Empty(nothingYet);
        Assert.Equal(TravelRequestStatus.InTransit, request.Status);
        Assert.Equal(ShuttleState.EnRoute, fleet.Shuttles[0].State);

        // Tick 2: still flying.
        Assert.Empty(fleet.AdvanceTo(T0 + TimeSpan.FromSeconds(1)));
        Assert.Equal(TravelRequestStatus.InTransit, request.Status);

        // Tick 3: arrival, unload, history.
        var history = fleet.AdvanceTo(T0 + (4 * BaseLeg));

        var entry = Assert.Single(history);
        Assert.Equal(TravelRequestStatus.Completed, entry.Outcome);
        Assert.Equal(ArgusX, entry.DestinationPlanetId);
        Assert.Equal(TravelRequestStatus.Completed, request.Status);
        Assert.Equal(ShuttleState.Idle, fleet.Shuttles[0].State);
        Assert.Equal(ArgusX, fleet.Shuttles[0].CurrentPlanetId);
    }
}
