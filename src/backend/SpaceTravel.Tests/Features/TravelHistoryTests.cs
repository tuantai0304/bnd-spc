using System.Net.Http.Json;
using System.Text.Json;

namespace SpaceTravel.Tests.Features;

/// <summary>
/// The brief's actual business goal: knowing which planets get travelled to, and
/// where calls are being turned away, so the team can decide where the next
/// shuttle goes. Each test owns its own database so counts are exact.
/// </summary>
public sealed class TravelHistoryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static object Call(int origin, int destination, params (string Species, decimal WeightKg)[] party)
        => new
        {
            originPlanetId = origin,
            destinationPlanetId = destination,
            lifeForms = party.Select(p => new { species = p.Species, weightKg = p.WeightKg })
        };

    [Fact]
    public async Task A_completed_trip_and_a_rejected_call_are_both_recorded_with_distinct_outcomes()
    {
        using var factory = new SpaceTravelApiFactory();
        var client = factory.CreateClient();

        // One trip that can fly: Angel 1 -> Argus X.
        await client.PostAsJsonAsync("/api/travel-requests", Call(1, 5, ("Vulcan", 68m)));

        // One that never can: too heavy for any shuttle.
        await client.PostAsJsonAsync("/api/travel-requests", Call(1, 3, ("Horta", 5000m)));

        await factory.RunToCompletionAsync();

        var page = await client.GetFromJsonAsync<JsonElement>(
            "/api/travel-history?page=1&pageSize=50", Json);

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());

        var outcomes = page.GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("outcome").GetString())
            .Order()
            .ToList();

        Assert.Equal(["Completed", "Rejected"], outcomes);
    }

    [Fact]
    public async Task Stats_group_by_destination_and_report_every_planet()
    {
        using var factory = new SpaceTravelApiFactory();
        var client = factory.CreateClient();

        // Two parties to Argus X, one to Boreth.
        await client.PostAsJsonAsync("/api/travel-requests", Call(1, 5, ("Vulcan", 68m)));
        await client.PostAsJsonAsync("/api/travel-requests", Call(1, 5, ("Andorian", 82m)));
        await client.PostAsJsonAsync("/api/travel-requests", Call(1, 2, ("Gorn", 210m)));

        // And one call to Aurelia that no shuttle could ever serve.
        await client.PostAsJsonAsync("/api/travel-requests", Call(1, 3, ("Horta", 9000m)));

        await factory.RunToCompletionAsync();

        var stats = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/travel-history/stats", Json);

        Assert.NotNull(stats);

        // Every planet appears, travelled or not — a zero is a finding.
        Assert.Equal(5, stats.Count);

        var byName = stats.ToDictionary(s => s.GetProperty("planet").GetString()!);

        Assert.Equal(2, byName["Argus X"].GetProperty("completedTrips").GetInt32());
        Assert.Equal(150m, byName["Argus X"].GetProperty("weightDeliveredKg").GetDecimal());
        Assert.Equal(2, byName["Argus X"].GetProperty("lifeFormsDelivered").GetInt32());

        Assert.Equal(1, byName["Boreth"].GetProperty("completedTrips").GetInt32());

        // The rejection is attributed to the destination that was asked for.
        Assert.Equal(1, byName["Aurelia"].GetProperty("rejectedCalls").GetInt32());
        Assert.Equal(0, byName["Aurelia"].GetProperty("completedTrips").GetInt32());

        Assert.Equal(0, byName["Blue Horizon"].GetProperty("completedTrips").GetInt32());

        // Most travelled first — the brief's headline question.
        Assert.Equal("Argus X", stats[0].GetProperty("planet").GetString());
    }

    [Fact]
    public async Task Two_parties_going_the_same_way_share_one_shuttle()
    {
        using var factory = new SpaceTravelApiFactory();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 5, ("Vulcan", 68m)));
        var second = await client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 5, ("Andorian", 82m)));

        var a = await first.Content.ReadFromJsonAsync<JsonElement>(Json);
        var b = await second.Content.ReadFromJsonAsync<JsonElement>(Json);

        // Batching: one launch, not two. This is the fuel requirement in action.
        Assert.Equal(
            a.GetProperty("shuttleId").GetInt32(),
            b.GetProperty("shuttleId").GetInt32());
    }

    [Fact]
    public async Task History_is_empty_before_anything_happens()
    {
        using var factory = new SpaceTravelApiFactory();
        var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/travel-history", Json);

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray());

        var stats = await client.GetFromJsonAsync<List<JsonElement>>(
            "/api/travel-history/stats", Json);

        Assert.NotNull(stats);
        Assert.All(stats, s => Assert.Equal(0, s.GetProperty("completedTrips").GetInt32()));
    }

    [Fact]
    public async Task Paging_returns_the_newest_entries_first()
    {
        using var factory = new SpaceTravelApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            // Each of these is rejected outright, so all five land in history at once.
            await client.PostAsJsonAsync(
                "/api/travel-requests", Call(1, 2, ($"Horta{i}", 9000m)));
        }

        var page = await client.GetFromJsonAsync<JsonElement>(
            "/api/travel-history?page=1&pageSize=2", Json);

        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        // Newest first: ids descend.
        Assert.True(items[0].GetProperty("id").GetInt32() > items[1].GetProperty("id").GetInt32());

        var secondPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/travel-history?page=2&pageSize=2", Json);

        Assert.Equal(2, secondPage.GetProperty("items").GetArrayLength());
    }
}
